using System.Net;

namespace FreshOnion.Tor.Http;

public readonly record struct ExitNodeInfo(string ExitNode, DateTimeOffset Published, DateTimeOffset LastStatus, IPAddress ExitAddress) { }