namespace FreshOnion;

public interface ITorExitNodeChecker
{
    Task<List<string>> SelectBestNodes(CancellationToken cancellationToken);
}