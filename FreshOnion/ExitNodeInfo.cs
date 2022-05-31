using System.Net;

namespace FreshOnion;

public readonly record struct ExitNodeInfo(string ExitNode, DateTimeOffset Published, DateTimeOffset LastStatus, IPAddress ExitAddress) { }