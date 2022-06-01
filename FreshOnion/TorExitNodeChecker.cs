using System.Net;
using System.Net.Sockets;
using FreshOnion.Tor;
using FreshOnion.Tor.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace FreshOnion;

public class TorExitNodeChecker : ITorExitNodeChecker
{
    private readonly IConfiguration _configuration;
    private readonly IConfigurationSection _freshOnionSection;
    private readonly IServiceProvider _container;
    private readonly ILogger<TorExitNodeChecker> _logger;
    private readonly IConfigurationSection _httpConfig;

    private static readonly List<string> DefaultUrlsToCheck = new List<string>()
    {
        //"https://alpha.thekingfisher.io/",
        "https://api.binance.com/api/v3/time",
        //"https://api3.binance.com/api/v3/exchangeInfo",
        "https://ftx.com/api/markets/BTC-PERP",
        //"https://www.jeuxvideo.com/forums/0-3011927-0-1-0-1-0-finance.htm"
    };

    public TorExitNodeChecker(IServiceProvider container, IConfiguration configuration,
        ILogger<TorExitNodeChecker> logger)
    {
        _container = container;
        _configuration = configuration;
        _logger = logger;
        _freshOnionSection = configuration.GetSection("FreshOnion");
        _httpConfig = configuration.GetSection("Http");
    }

    private async Task<bool> CheckNode(ExitNodeInfo node, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var tor = _container.GetService<ITorService>()!;
        await tor.Start(token).ConfigureAwait(false); // TODO reuse tor

        var policy = Policy.Handle<SocketException>().WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(i * 5));
        await policy
            .ExecuteAsync(cancellationToken => tor.ChangeExitNodes(new[] { node.ExitNode }, cancellationToken), token)
            .ConfigureAwait(false);

        using var client = new HttpClient(new HttpClientHandler() { Proxy = tor.WebProxy });
        var userAgent =
            _httpConfig.GetValue<string>("UserAgent",
                "Mozilla/5.0 (Windows NT 10.0; rv:91.0) Gecko/20100101 Firefox/91.0");
        //client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(_freshOnionSection.GetValue<int>("CheckTaskInnerTimeout", 60_000));

        var urlToCheck = "https://check.torproject.org/";
        urlToCheck = _freshOnionSection.GetValue<string>("CheckConnectionUrl", urlToCheck);

        while (!cts.IsCancellationRequested)
        {
            try
            {
                var r = await client.GetAsync(urlToCheck, cts.Token);
                if (r.IsSuccessStatusCode) break;
            }
            catch (Exception e) when (e is TaskCanceledException or OperationCanceledException or TimeoutException)
            {
                _logger.LogTrace(e, "tor still not started");
                if (cts.IsCancellationRequested) break;
                await Task.Delay(500, cts.Token);
            }
        }

        if (cts.IsCancellationRequested)
        {
            _logger.LogWarning("Unable to start tor in time");
            return false;
        }

        _logger.LogDebug("started tor {Id} for {Node}", tor.Id, node.ExitNode);

        var urls = _freshOnionSection.GetValue<List<string>>("UrlsToCheck", DefaultUrlsToCheck);

        var counter = 0;
        var httpPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.NotFound)
            .WaitAndRetryAsync(_httpConfig.GetValue("NumRetry", 2), retryAttempt =>
                TimeSpan.FromMilliseconds(Math.Pow(2,
                    retryAttempt)) * _httpConfig.GetValue("RetryMs", 2000));

        try
        {
            await Parallel.ForEachAsync(urls, cts.Token, async (url, cancellationToken) =>
            {
                await httpPolicy.ExecuteAsync(async token1 =>
                {
                    using var r = await client.GetAsync(url, token1).ConfigureAwait(false);
                    if (!r.IsSuccessStatusCode) return r;
                    var s = await r.Content.ReadAsStringAsync(token1).ConfigureAwait(false);
                    if (s.Contains("CloudFlare", StringComparison.InvariantCultureIgnoreCase))
                    {
                        return r;
                    }

                    Interlocked.Increment(ref counter);
                    return r;
                }, cancellationToken);
            });
        }
        catch (Exception e) when (e is TaskCanceledException or OperationCanceledException or HttpRequestException)
        {
            _logger.LogTrace(e, "");
        }
        _logger.LogDebug("{Node} score: {Score:.02f}", node.ExitNode, 1.0f * counter / urls.Count);
        return counter * 10 > 8 * urls.Count;
    }

    public async Task<HashSet<string>> SelectBestNodes(CancellationToken cancellationToken)
    {
        var maxTasks = _freshOnionSection.GetValue<int>("NumCheckTask", 2);

        using var semaphore = new SemaphoreSlim(maxTasks, maxTasks);

        var urls = await _container.GetService<IExitListService>()!.GetExitListsUrls(cancellationToken)
            .ConfigureAwait(false);
        _logger.LogInformation("Gathered {Count} urls", urls.Count);

        var nodes = await _container.GetService<IExitListService>()!.GetExitNodeInfos(
            new Uri(urls.MaxBy(tuple => tuple.date).url), cancellationToken);
        nodes.Sort((left, right) => right.Published.CompareTo(left.Published));
        _logger.LogInformation("Gathered {Count} nodes", nodes.Count);

        var result = new HashSet<string>();
        try
        {
            await Parallel.ForEachAsync(nodes.Take(16), cancellationToken, async (node, token) =>
            {
                await semaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (await CheckNode(node, token).ConfigureAwait(false))
                    {
                        lock (result)
                        {
                            result.Add(node.ExitNode);
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });
        }
        catch (Exception e) when (e is TaskCanceledException or OperationCanceledException or HttpRequestException)
        {
            _logger.LogTrace(e, "");
            if (!cancellationToken.IsCancellationRequested) throw;
        }

        return result;
    }
}