namespace FreshOnion.Tor.ControlPort;

public readonly record struct ControlClientOptions(int Port, Memory<byte> Password, string Host = "localhost");