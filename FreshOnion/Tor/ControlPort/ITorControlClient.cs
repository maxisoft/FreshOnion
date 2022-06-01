namespace FreshOnion.Tor.ControlPort;

public interface ITorControlClient
{
    Task<string> ChangeExitNodes(IEnumerable<string> exitNodes, CancellationToken cancellationToken);
}