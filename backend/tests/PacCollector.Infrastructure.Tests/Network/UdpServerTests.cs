using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using NSubstitute;
using PacCollector.Application.UseCases;
using PacCollector.Domain.Ports;
using PacCollector.Infrastructure.Network;

namespace PacCollector.Infrastructure.Tests.Network;

public class UdpServerTests
{
    [Fact]
    public async Task BeaconGetsAckedWithConfiguredIpAndTcpPort()
    {
        var bus = Substitute.For<IEventBus>();
        var handler = new HandleBeaconUseCase(bus);
        var port = GetFreeUdpPort();
        var bind = new IPEndPoint(IPAddress.Loopback, port);
        var server = new UdpServer(bind, configuredIp: "10.20.30.40", tcpPort: 9980, handler);

        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));
        await Task.Delay(100); // dar tiempo a bindear

        using var client = new UdpClient(0, AddressFamily.InterNetwork);
        await client.SendAsync(Protocol.Beacon, Protocol.Beacon.Length, "127.0.0.1", port);

        var receive = client.ReceiveAsync();
        var winner = await Task.WhenAny(receive, Task.Delay(3000));
        winner.Should().BeSameAs(receive, "el server tiene que responder dentro de 3s");

        var ack = Encoding.ASCII.GetString((await receive).Buffer);
        ack.Should().Be("ACK 10.20.30.40 9980");

        // poll-wait: el handle_beacon corre despues del SendAsync; puede no haber publicado todavia
        await WaitUntil(() => bus.ReceivedCalls().Any(), TimeSpan.FromSeconds(2));
        bus.Received(1).Publish(Arg.Any<DomainEvent.BeaconReceived>());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { /* expected */ }
    }

    [Fact]
    public async Task NonBeaconPayloadGetsNak()
    {
        var bus = Substitute.For<IEventBus>();
        var handler = new HandleBeaconUseCase(bus);
        var port = GetFreeUdpPort();
        var bind = new IPEndPoint(IPAddress.Loopback, port);
        var server = new UdpServer(bind, configuredIp: "127.0.0.1", tcpPort: 9980, handler);

        using var cts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(cts.Token));
        await Task.Delay(100);

        using var client = new UdpClient(0, AddressFamily.InterNetwork);
        var nonBeacon = Encoding.ASCII.GetBytes("hello there");
        await client.SendAsync(nonBeacon, nonBeacon.Length, "127.0.0.1", port);

        var receive = client.ReceiveAsync();
        var winner = await Task.WhenAny(receive, Task.Delay(3000));
        winner.Should().BeSameAs(receive);
        Encoding.ASCII.GetString((await receive).Buffer).Should().Be("NAK");

        // dar un toque mas para confirmar que no se publica BeaconReceived
        await Task.Delay(100);
        bus.DidNotReceive().Publish(Arg.Any<DomainEvent.BeaconReceived>());

        cts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { /* expected */ }
    }

    private static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(20);
        }
    }

    private static int GetFreeUdpPort()
    {
        using var udp = new UdpClient(0, AddressFamily.InterNetwork);
        return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
    }
}
