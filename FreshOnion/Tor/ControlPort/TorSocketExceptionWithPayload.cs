namespace FreshOnion.Tor.ControlPort;

public class TorSocketExceptionWithPayload : TorSocketException
{
    public readonly ReadOnlyMemory<byte> Payload;
    public TorSocketExceptionWithPayload() { }

    public TorSocketExceptionWithPayload(ReadOnlyMemory<byte> payload, string? message, Exception? innerException) :
        base(
            message, innerException)
    {
        Payload = payload;
    }
}