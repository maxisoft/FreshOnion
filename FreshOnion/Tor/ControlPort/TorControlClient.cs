using System.Buffers;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using static FreshOnion.Tor.ControlPort.TorControlClientMessage;

namespace FreshOnion.Tor.ControlPort;

public class TorControlClient : ITorControlClient
{
    private readonly ILogger<TorControlClient> _logger;
    private readonly ControlClientOptions _options;

    public TorControlClient(ControlClientOptions options, ILogger<TorControlClient> logger)
    {
        var memory = Memory<byte>.Empty;
        if (!options.Password.IsEmpty)
        {
            var hex = Convert.ToHexString(options.Password.Span);
            // TODO bad security here as hex is a managed string
            // it may be in memory for a long time
            memory = Encoding.ASCII.GetBytes(hex);
        }
        Xor(memory.Span);
        _logger = logger;
        _options = options with { Password = memory };
    }

    private static void Xor(Span<byte> bytes, byte salt = 0x66)
    {
        for (var i = 0; i < bytes.Length; i++) bytes[i] ^= salt;
    }

    internal static async Task<Socket> Connect(int port, CancellationToken cancellationToken, string host = "localhost")
    {
        var sender = new Socket(
            SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await sender.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            sender.Dispose();
            throw;
        }

        return sender;
    }

    private int WritePassword(Span<byte> output)
    {
        _options.Password.Span.CopyTo(output);
        var len = _options.Password.Length;
        Xor(output[..len]);
        return len;
    }

    public async Task<string> ChangeExitNodes(IEnumerable<string> exitNodes, CancellationToken cancellationToken)
    {
        using var s = await Connect(_options.Port, cancellationToken, _options.Host).ConfigureAwait(false);
        Debug.Assert(s.Connected);

        using var msgBuffer = MemoryPool<byte>.Shared.Rent(8 << 10);
        var memory = msgBuffer.Memory;
        var c = Encoding.ASCII.GetBytes("authenticate ", msgBuffer.Memory.Span);
        if (_options.Password.Length > 0)
        {
            c += WritePassword(memory.Span[c..]);
            EndMessagePart.CopyTo(memory[c..]);
            c += EndMessagePart.Length;
        }
        else
        {
            EmptyPasswordMessagePart.CopyTo(memory[c..]);
            c += EmptyPasswordMessagePart.Length;
        }

        int sent;

        try
        {
            sent = await s.SendAsync(memory[..c], SocketFlags.None, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            memory[..c].Span.Clear();
        }

        if (sent != c)
            throw new TorSocketExceptionWithPayload(msgBuffer.Memory[..c].ToArray(), "unable to send authenticate",
                null);

        if (!s.Connected)
            throw new TorSocketExceptionWithPayload(msgBuffer.Memory[..c].ToArray(), "remote disconnected", null);

        c = await s.ReceiveAsync(memory, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        if (c >= memory.Length)
            throw new TorSocketExceptionWithPayload(memory.ToArray(), "message buffer overflow", null);

        if (!msgBuffer.Memory.Span[..c].SequenceEqual(OkMessage))
            throw new TorSocketExceptionWithPayload(memory[..c].ToArray(), "Unable to auth", null);

        sent = await s.SendAsync(GetExitNodesMessage, SocketFlags.None, cancellationToken).ConfigureAwait(false);

        if (sent != GetExitNodesMessage.Length)
            throw new TorSocketExceptionWithPayload(GetExitNodesMessage.ToArray(), "unable to get exit node message",
                null);

        c = await s.ReceiveAsync(memory, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        if (c >= memory.Length)
            throw new TorSocketExceptionWithPayload(memory.ToArray(), "message buffer overflow", null);

        if (!memory[..c].Span.StartsWith(ExitNodesOkMessagePart))
            throw new TorSocketExceptionWithPayload(memory[..c].ToArray(), "unable to get exit nodes", null);

        var prevExitNodes =
            Encoding.UTF8.GetString(memory.Span[..c][ExitNodesOkMessagePart.Length..].TrimEnd(EndMessagePart));

        c = Encoding.ASCII.GetBytes("setconf ExitNodes=", memory.Span);
        foreach (var exitNode in exitNodes)
        {
            c += Encoding.UTF8.GetBytes(exitNode, memory.Span[c..]);
            if (c >= memory.Length)
                throw new TorSocketExceptionWithPayload(memory[..c].ToArray(), "unable to write all exitNodes", null);

            memory.Span[c] = (byte)',';
            c++;
        }

        c = memory.Span[..c].TrimEnd((byte)',').Length;
        EndMessagePart.CopyTo(memory[c..]);
        c += EndMessagePart.Length;

        sent = await s.SendAsync(memory[..c], SocketFlags.None, cancellationToken).ConfigureAwait(false);
        if (sent != c)
            throw new TorSocketExceptionWithPayload(GetExitNodesMessage.ToArray(), "unable to send setconf ExitNodes",
                null);

        c = await s.ReceiveAsync(memory, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        if (c >= memory.Length)
            throw new TorSocketExceptionWithPayload(memory.ToArray(), "message buffer overflow", null);

        if (!memory[..c].Span.SequenceEqual(OkMessage))
            throw new TorSocketExceptionWithPayload(memory[..c].ToArray(), "unable to set exit nodes", null);

        sent = await s.SendAsync(ReloadCircuitsMessage, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        if (sent != ReloadCircuitsMessage.Length)
            throw new TorSocketExceptionWithPayload(GetExitNodesMessage.ToArray(),
                "unable to send reload circuits signal", null);

        c = await s.ReceiveAsync(memory, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        if (c >= memory.Length)
            throw new TorSocketExceptionWithPayload(memory.ToArray(), "message buffer overflow", null);

        if (!memory[..c].Span.SequenceEqual(OkMessage))
            throw new TorSocketExceptionWithPayload(memory[..c].ToArray(), "unable to reload circuits", null);

        await s.DisconnectAsync(false, cancellationToken).ConfigureAwait(false);

        return prevExitNodes;
    }
}