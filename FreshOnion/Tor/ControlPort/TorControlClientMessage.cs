using System.Text;

namespace FreshOnion.Tor.ControlPort;

public static class TorControlClientMessage
{
    internal static readonly byte[] EndMessagePart = Encoding.UTF8.GetBytes("\r\n");
    internal static readonly byte[] EmptyPasswordMessagePart = Encoding.UTF8.GetBytes("\"\"\n");
    internal static readonly byte[] OkMessage = Encoding.UTF8.GetBytes("250 OK\r\n");
    internal static readonly byte[] GetExitNodesMessage = Encoding.UTF8.GetBytes("getconf ExitNodes\r\n");
    internal static readonly byte[] ExitNodesOkMessagePart = Encoding.UTF8.GetBytes("250 ExitNodes=");
    internal static readonly byte[] ReloadCircuitsMessage = Encoding.UTF8.GetBytes("signal newnym\r\n");
}