namespace FreshOnion.Tor.Http;

public interface IExitListService
{
    public ValueTask<List<(string url, DateTimeOffset date)>> GetExitListsUrls(CancellationToken cancellationToken);
    public ValueTask<List<ExitNodeInfo>> GetExitNodeInfos(Uri uri, CancellationToken cancellationToken);
}