using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PacCollector.Api.Services;

// shape consumido por el frontend NetworkDiagnostics: una entry por (interface, ipv4)
// con netmask + prefix len. Si una interfaz tiene varias IPs sale una entry por cada una.
public sealed record NetInterface(
    string Name,
    string Ip,
    string Netmask,
    int PrefixLen,
    bool IsLinkLocal);

public sealed class NetworkInfoService
{
    public IReadOnlyList<string> ListLocalIps()
    {
        var ips = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    ips.Add(addr.Address.ToString());
                }
            }
        }
        catch { /* best-effort */ }
        return ips;
    }

    public IReadOnlyList<NetInterface> ListInterfaces()
    {
        var result = new List<NetInterface>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                    var ip = addr.Address.ToString();
                    var prefix = addr.PrefixLength;
                    var netmask = PrefixToNetmask(prefix);
                    var isLinkLocal = IsLinkLocalIPv4(addr.Address);

                    result.Add(new NetInterface(
                        Name: nic.Name,
                        Ip: ip,
                        Netmask: netmask,
                        PrefixLen: prefix,
                        IsLinkLocal: isLinkLocal));
                }
            }
        }
        catch { /* best-effort */ }
        return result;
    }

    // convierte prefix length a netmask dotted-decimal (ej. 24 -> 255.255.255.0)
    private static string PrefixToNetmask(int prefixLen)
    {
        if (prefixLen <= 0) return "0.0.0.0";
        if (prefixLen >= 32) return "255.255.255.255";
        uint mask = 0xFFFFFFFFu << (32 - prefixLen);
        return $"{(mask >> 24) & 0xFF}.{(mask >> 16) & 0xFF}.{(mask >> 8) & 0xFF}.{mask & 0xFF}";
    }

    // RFC 3927: 169.254.0.0/16 es link-local IPv4 (APIPA / auto-config)
    private static bool IsLinkLocalIPv4(IPAddress addr)
    {
        var bytes = addr.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }
}
