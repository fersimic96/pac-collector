using FluentAssertions;
using NSubstitute;
using PacCollector.Application.UseCases;
using PacCollector.Domain.Ports;

namespace PacCollector.Application.Tests.UseCases;

public class HandleBeaconUseCaseTests
{
    [Fact]
    public void Execute_PublishesBeaconReceivedEvent()
    {
        var bus = Substitute.For<IEventBus>();
        var sut = new HandleBeaconUseCase(bus);

        sut.Execute("192.168.1.10");

        bus.Received(1).Publish(Arg.Is<DomainEvent.BeaconReceived>(e => e.Ip == "192.168.1.10"));
    }
}
