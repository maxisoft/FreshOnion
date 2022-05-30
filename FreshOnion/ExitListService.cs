using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace FreshOnion;

public interface IExitListService
{
    public ValueTask<List<(string url, DateTimeOffset date)>> GetExitListsUrls(CancellationToken cancellationToken);
    public ValueTask<List<ExitNodeInfo>> GetExitNodeInfos(Uri uri, CancellationToken cancellationToken);
}

public class ExitListService: IExitListService
{
    private readonly ILogger<ExitListService> _logger;
    private readonly HttpClient _httpClient;

    private static readonly Regex urlRegex =
        new Regex(@"https?://(?:.*?)/recent/exit-lists/(?<date>\d+-\d+-\d+-\d+-\d+-\d+)",
            RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public ExitListService(ILoggerFactory loggerFactory, HttpClient httpClient)
    {
        _logger = loggerFactory.CreateLogger<ExitListService>();
        _httpClient = httpClient;
    }

    public async ValueTask<List<(string url, DateTimeOffset date)>> GetExitListsUrls(CancellationToken cancellationToken)
    {
        using var r = await _httpClient.GetAsync("https://metrics.torproject.org/collector/recent/exit-lists/", cancellationToken);
        var content = await r.Content.ReadAsStringAsync(cancellationToken);
        var res = new List<(string url, DateTimeOffset date)>();
        foreach (Match match in urlRegex.Matches(content))
        {
            if (!match.Success) continue;
            var dateGroup = match.Groups["date"];
            if (DateTimeOffset.TryParseExact(dateGroup.Value, "yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                date = date.ToLocalTime();
                if (res.Any() && res[^1].date == date) continue; 
                res.Add((match.Value, date));
            }
            
            
        }
        return res;
    }

    public async ValueTask<List<ExitNodeInfo>> GetExitNodeInfos(Uri uri, CancellationToken cancellationToken)
    {
        using var r = await _httpClient.GetAsync(uri, cancellationToken);
        if (!TordnselParser.TryParse(await r.Content.ReadAsStringAsync(cancellationToken), out var result))
        {
            _logger.LogError("Unable to parse node infos from {Uri}", uri);
        }

        return result;
    }
}