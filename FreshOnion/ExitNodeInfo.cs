namespace FreshOnion;

public readonly record struct ExitNodeInfo(string ExitNode, DateTimeOffset Published, DateTimeOffset LastStatus, string ExitAddress) { }