// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using System.Runtime.InteropServices;
using FreshOnion.FileSystem;
using FreshOnion.IoC;
using FreshOnion.Tor;
using FreshOnion.Tor.ControlPort;
using FreshOnion.Tor.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Polly;

namespace FreshOnion;

class Program
{
    public static async Task Main(string[] args)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var configurationRoot = BuildConfiguration();
        var serviceProvider = BuildServiceProvider(configurationRoot);
        var logger = serviceProvider.GetService<ILoggerFactory>()!.CreateLogger<Program>();
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        void CancelMainToken()
        {
            try
            {
                // ReSharper disable once AccessToDisposedClosure
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                Debug.WriteLine("Trying to cancel disposed CancellationTokenSource");
            }
        }

        Console.CancelKeyPress += (_, _) => CancelMainToken();

        using var torInstance = serviceProvider.GetRequiredService<ITorServiceSingleton>();
        // ReSharper disable once AccessToDisposedClosure
        await using var cancellationTokenRegistration = cancellationToken.Register(() => torInstance.Dispose());
        logger.LogDebug("Starting tor main instance ...");
        await torInstance.Start(cts.Token);
        logger.LogInformation("Started tor main instance on {Address}", torInstance.WebProxy.Address);

        {
            logger.LogDebug("Waiting for tor circuits to init ...");
            using var waitFirstConnection = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            waitFirstConnection.CancelAfter(
                configurationRoot.GetSection("Tor").GetValue<int>("CircuitTimeout", 100_000));

            while (!waitFirstConnection.IsCancellationRequested)
            {
                try
                {
                    await serviceProvider.GetRequiredService<IExitListService>()
                        .GetExitListsUrls(waitFirstConnection.Token)
                        .ConfigureAwait(false);
                    break;
                }
                catch (TaskCanceledException e)
                {
                    logger.LogTrace(e, "tor still not started ...");
                    if (waitFirstConnection.IsCancellationRequested)
                    {
                        throw;
                    }

                    await Task.Delay(500, cts.Token);
                }
            }

            waitFirstConnection.Token.ThrowIfCancellationRequested();
        }

        var runId = 0;
        while (!(torInstance.Process?.HasExited ?? true))
        {
            using var tickCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            tickCancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(runId <= 0 ? 2 : 15));

            List<(string url, DateTimeOffset date)> urls;
            List<ExitNodeInfo> nodes;
            try
            {
                urls = await serviceProvider.GetRequiredService<IExitListService>()
                    .GetExitListsUrls(tickCancellationTokenSource.Token);
                logger.LogInformation("Gathered {Count} urls", urls.Count);

                nodes = await serviceProvider.GetRequiredService<IExitListService>().GetExitNodeInfos(
                    new Uri(urls.MaxBy(tuple => tuple.date).url), tickCancellationTokenSource.Token);
                nodes.Sort(static (left, right) => right.Published.CompareTo(left.Published));
            }
            catch (Exception e) when (e is OperationCanceledException or HttpRequestException)
            {
                logger.LogError(e, "Unable to retrieve exit nodes, restarting tor");
                await torInstance.Restart(cts.Token);
                continue;
            }


            if (runId <= 0)
            {
                foreach (var nodeInfo in nodes.Take(5))
                {
                    logger.LogInformation("node {Node}", nodeInfo);
                }

                logger.LogInformation("Gathered {Count} nodes", nodes.Count);
            }


            List<string> validNodes;
            using (var bestNodeCancellationTokenSource =
                   CancellationTokenSource.CreateLinkedTokenSource(tickCancellationTokenSource.Token))
            {
                bestNodeCancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(runId <= 0 ? 1 : 15));

                validNodes = await serviceProvider.GetService<ITorExitNodeChecker>()!
                    .SelectBestNodes(bestNodeCancellationTokenSource.Token)
                    .ConfigureAwait(false);
            }


            logger.LogInformation("Found {Count} valid nodes", validNodes.Count);
            runId++;
            if (validNodes.Any())
            {
                await torInstance.ChangeExitNodes(validNodes.Take(128), tickCancellationTokenSource.Token)
                    .ConfigureAwait(false);
            }
            else
            {
                logger.LogError("Unable to find any valid nodes, restarting tor");
                await torInstance.Restart(tickCancellationTokenSource.Token).ConfigureAwait(false);
                continue;
            }


            if (torInstance.Process is not { HasExited: false }) continue;
            var processExitTask = torInstance.Process.WaitForExitAsync(tickCancellationTokenSource.Token);
            var monitorTask = MonitorTor(serviceProvider, tickCancellationTokenSource.Token);
            try
            {
                await Task.WhenAny(processExitTask, monitorTask).ConfigureAwait(false);
                if (monitorTask.IsFaulted)
                {
                    logger.LogError(monitorTask.Exception, "error while monitoring tor");
                    await torInstance.Restart(tickCancellationTokenSource.Token).ConfigureAwait(false);
                    continue;
                }

                await Task.WhenAll(monitorTask, processExitTask).ConfigureAwait(false);
                monitorTask.Dispose();
                processExitTask.Dispose();
            }
            catch (Exception e) when (e is OperationCanceledException or TimeoutException)
            {
                tickCancellationTokenSource.Cancel();
                logger.LogTrace("tor still running ...");
            }
        }

        await torInstance.Process!.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        Environment.Exit(cts.IsCancellationRequested ? 1 : torInstance.Process.ExitCode);
    }

    private static async Task MonitorTor(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(1));
            try
            {
                await serviceProvider.GetRequiredService<IExitListService>().GetExitListsUrls(cts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                throw;
            }

            await Task.Delay(30_000, cancellationToken).ConfigureAwait(false);
        }
    }

    private static ServiceProvider BuildServiceProvider(IConfigurationRoot configurationRoot)
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging(builder =>
            {
                builder
                    .AddConfiguration(configurationRoot.GetSection("Logging"))
                    .AddSystemdConsole()
#if DEBUG
                    .AddDebug()
#endif
                    ;
            })
            .AddSingleton<ITorServiceSingleton, MainTorService>()
            .AddTransient<IHttpClientFactoryHelper, HttpClientFactoryHelper>()
            .AddHttpClient<IExitListService, ExitListService>((provider, client) =>
            {
                var httpClientFactoryHelper = provider.GetRequiredService<IHttpClientFactoryHelper>();
                httpClientFactoryHelper.Configure(client);
            })
            .ConfigurePrimaryHttpMessageHandler(provider =>
                provider.GetRequiredService<IHttpClientFactoryHelper>().GetHandler(provider
                    .GetService<ITorServiceSingleton>()
                    ?.WebProxy))
            .AddPolicyHandler((provider, _) => provider.GetRequiredService<IHttpClientFactoryHelper>().GetRetryPolicy())
            .Services
            .AddOptions()
            .AddSingleton<IConfiguration>(configurationRoot)
            .AddSingleton(configurationRoot)
            .AddTransient<IFileSearch, FileSearch>()
            .AddTransient<ITorService, TorService>()
            .AddTransient<ITorConfigurationFileGenerator, TorConfigurationFileGenerator>()
            .AddTransient<ITorExitNodeChecker, TorExitNodeChecker>()
            .BuildServiceProvider();
        return serviceProvider;
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var configurationRoot = new ConfigurationBuilder()
            .AddEnvironmentVariables("FRESHONION_")
            .AddJsonFile("appsettings.json", true)
            .Build();
        return configurationRoot;
    }
}