using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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

    public TorExitNodeChecker(IServiceProvider container, IConfiguration configuration,
        ILogger<TorExitNodeChecker> logger)
    {
        _container = container;
        _configuration = configuration;
        _logger = logger;
        _freshOnionSection = configuration.GetSection("FreshOnion");
    }

    private async Task<long> CheckNode(ExitNodeInfo node, CancellationToken token)
    {
        var pingTimeout = _freshOnionSection.GetValue<int>("PingTimeout", 5_000);
        var ttl = _freshOnionSection.GetValue<int>("PingTtl", 32);
        var pingCount = _freshOnionSection.GetValue<int>("PingCount", 5);
        var sw = Stopwatch.StartNew();
        using var pingSender = new Ping();
        var buff = new byte[32];
        var h = node.GetHashCode();

        for (var i = 0; i < buff.Length; i++)
        {
            buff[i] = (byte)((h >> i) ^ (h * 31) ^ i);
        }

        for (var i = 0; i < pingCount; i++)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var r = await pingSender.SendPingAsync(node.ExitAddress, pingTimeout, buff, new PingOptions(ttl, true))
                        .ConfigureAwait(false);
                    if (!((ReadOnlySpan<byte>)r.Buffer).SequenceEqual(buff))
                    {
                        return -1;
                    }
                }
                else
                {
                    await pingSender.SendPingAsync(node.ExitAddress, pingTimeout)
                        .ConfigureAwait(false);
                }
                
            }
            catch (PingException)
            {
                return -1;
            }
            catch (Exception e) when (e is TaskCanceledException or OperationCanceledException or SocketException)
            {
                if (!token.IsCancellationRequested) throw;
                _logger.LogTrace(e, "");
                return -1;
            }
        }

        return sw.ElapsedMilliseconds;
    }

    public async Task<HashSet<string>> SelectBestNodes(CancellationToken cancellationToken)
    {
        var maxTasks = 256;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            maxTasks = 64;
        }

        maxTasks = _freshOnionSection.GetValue<int>("NumCheckTask", maxTasks);

        using var semaphore = new SemaphoreSlim(maxTasks, maxTasks);

        var urls = await _container.GetService<IExitListService>()!.GetExitListsUrls(cancellationToken)
            .ConfigureAwait(false);
        _logger.LogDebug("Gathered {Count} urls", urls.Count);

        var nodes = await _container.GetService<IExitListService>()!.GetExitNodeInfos(
            new Uri(urls.MaxBy(tuple => tuple.date).url), cancellationToken);
        nodes.Sort((left, right) => right.Published.CompareTo(left.Published));
        _logger.LogDebug("Gathered {Count} nodes", nodes.Count);

        var result = new ConcurrentBag<(ExitNodeInfo node, long latency)>();
        try
        {
            await Parallel.ForEachAsync(nodes.Take(maxTasks), cancellationToken, async (node, token) =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (token.IsCancellationRequested) return;
                    var latency = await CheckNode(node, token).ConfigureAwait(false);
                    if (latency > 0)
                    {
                        result.Add((node, latency));
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

        return result
            .OrderBy(tuple => tuple.latency * Math.Log((DateTimeOffset.UtcNow - tuple.node.Published).Duration().TotalMinutes + 1))
            .Take(32)
            .Select(tuple => tuple.node.ExitNode).ToHashSet();
    }
}