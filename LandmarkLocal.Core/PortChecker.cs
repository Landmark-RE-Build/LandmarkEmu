using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LandmarkLocal;

public enum PortProtocol
{
    Tcp,
    Udp,
}

public readonly record struct PortNeed(string Name, int Port, PortProtocol Protocol);

public sealed class PortConflict
{
    public required string Name { get; init; }
    public required int Port { get; init; }
    public required PortProtocol Protocol { get; init; }
    public required string Detail { get; init; }
}

/// <summary>Generic in-use detection for LandmarkLocal's loopback ports.</summary>
public static class PortChecker
{
    public static IReadOnlyList<PortNeed> RequiredPorts(LocalSettings settings) =>
    [
        new("MariaDB", settings.DbPort, PortProtocol.Tcp),
        new("Auth account API", settings.AccountPort, PortProtocol.Tcp),
        new("Auth login", settings.LoginPort, PortProtocol.Udp),
        new("World", settings.WorldPort, PortProtocol.Udp),
        new("World web pages", settings.WebPagesPort, PortProtocol.Tcp),
    ];

    public static List<PortConflict> FindConflicts(IEnumerable<PortNeed> needs, IPAddress? bind = null)
    {
        bind ??= IPAddress.Loopback;
        var conflicts = new List<PortConflict>();
        foreach (PortNeed need in needs)
        {
            if (need.Protocol == PortProtocol.Tcp)
            {
                if (!CanBindTcp(bind, need.Port, out string detail))
                    conflicts.Add(new PortConflict { Name = need.Name, Port = need.Port, Protocol = need.Protocol, Detail = detail });
            }
            else
            {
                if (!CanBindUdp(bind, need.Port, out string detail))
                    conflicts.Add(new PortConflict { Name = need.Name, Port = need.Port, Protocol = need.Protocol, Detail = detail });
            }
        }
        return conflicts;
    }

    public static bool CanBindTcp(IPAddress address, int port, out string detail)
    {
        detail = "";
        try
        {
            using var listener = new TcpListener(address, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException ex)
        {
            detail = DescribeOccupant(port, PortProtocol.Tcp) ?? ex.Message;
            return false;
        }
    }

    public static bool CanBindUdp(IPAddress address, int port, out string detail)
    {
        detail = "";
        try
        {
            using var udp = new UdpClient(new IPEndPoint(address, port));
            return true;
        }
        catch (SocketException ex)
        {
            detail = DescribeOccupant(port, PortProtocol.Udp) ?? ex.Message;
            return false;
        }
    }

    static string? DescribeOccupant(int port, PortProtocol protocol)
    {
        try
        {
            IPGlobalProperties props = IPGlobalProperties.GetIPGlobalProperties();
            if (protocol == PortProtocol.Tcp)
            {
                foreach (IPEndPoint ep in props.GetActiveTcpListeners())
                {
                    if (ep.Port == port)
                        return $"TCP port {port} is already in use";
                }
            }
            else
            {
                foreach (IPEndPoint ep in props.GetActiveUdpListeners())
                {
                    if (ep.Port == port)
                        return $"UDP port {port} is already in use";
                }
            }
        }
        catch
        {
            // Fall through to socket error message.
        }
        return null;
    }
}
