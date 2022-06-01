namespace FreshOnion;

public interface ITorExitNodeChecker
{
    Task<HashSet<string>> SelectBestNodes(CancellationToken cancellationToken);
}