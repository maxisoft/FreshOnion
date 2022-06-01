using System.Runtime.Serialization;

namespace FreshOnion.Tor.ControlPort;

public class TorSocketException : Exception
{
    public TorSocketException() { }
    protected TorSocketException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    public TorSocketException(string? message) : base(message) { }
    public TorSocketException(string? message, Exception? innerException) : base(message, innerException) { }
}