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

        var configurationRoot = BuidConfiguration();
        var serviceProvider = BuildServiceProvider(configurationRoot);
        var logger = serviceProvider.GetService<ILoggerFactory>()!.CreateLogger<Program>();
        using var cts = new CancellationTokenSource();

        using var torInstance = serviceProvider.GetService<ITorServiceSingleton>()!;
        logger.LogDebug("Starting tor main instance ...");
        await torInstance.Start(cts.Token);
        torInstance.Process!.Exited += (sender, eventArgs) =>
            Policy
                .Handle<ObjectDisposedException>()
                .Fallback(() => { logger.LogCritical("closing error"); })
                .Execute(() => cts.Cancel()
                );
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
                    await serviceProvider.GetService<IExitListService>()!.GetExitListsUrls(waitFirstConnection.Token)
                        .ConfigureAwait(false);
                    break;
                }
                catch (Exception e) when (e is TaskCanceledException or OperationCanceledException or TimeoutException)
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

        var urls = await serviceProvider.GetService<IExitListService>()!.GetExitListsUrls(cts.Token);
        logger.LogInformation("Gathered {Count} urls", urls.Count);

        var nodes = await serviceProvider.GetService<IExitListService>()!.GetExitNodeInfos(
            new Uri(urls.MaxBy(tuple => tuple.date).url), cts.Token);
        nodes.Sort((left, right) => right.Published.CompareTo(left.Published));
        foreach (var nodeInfo in nodes.Take(5))
        {
            logger.LogInformation("node {Node}", nodeInfo);
        }

        logger.LogInformation("Gathered {Count} nodes", nodes.Count);


        while (!(torInstance.Process?.HasExited ?? true))
        {
            using var tickCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            tickCancellationTokenSource.CancelAfter(TimeSpan.FromMinutes(15));
            var validNodes = await serviceProvider.GetService<ITorExitNodeChecker>()!
                .SelectBestNodes(tickCancellationTokenSource.Token)
                .ConfigureAwait(false);
            logger.LogInformation("Found {Count} valid nodes", validNodes.Count);
            if (validNodes.Any())
            {
                await torInstance.ChangeExitNodes(validNodes, tickCancellationTokenSource.Token).ConfigureAwait(false);
            }

            if (torInstance.Process is not { HasExited: false }) continue;
            try
            {
                await torInstance.Process.WaitForExitAsync(tickCancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is TaskCanceledException or OperationCanceledException or TimeoutException)
            {
                logger.LogTrace("tor still running ...");
            }
        }

        await torInstance.Process!.WaitForExitAsync(cts.Token).ConfigureAwait(false);

        var tmp = Path.GetTempPath();
        var runtimeTmp = Path.Combine(tmp, "freshonion", Guid.NewGuid().ToString());

        Directory.CreateDirectory(runtimeTmp);
        var torrc = Path.Combine(runtimeTmp, "torrc");
        var torrcConfig = new TorConfiguration()
        {
            ExitNodes = nodes.Take(5).Select(info => info.ExitNode), SocksPort = 15578, ControlPort = 15579,
            CookieAuthFile = "cookie-auth"
        };

        {
            var config =
                await serviceProvider.GetService<ITorConfigurationFileGenerator>()!.Generate(torrcConfig, cts.Token);
            await File.WriteAllTextAsync(torrc, config, cts.Token);
        }

        var torExe = "tor";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            torExe += ".exe";
        }

        var torConfig = serviceProvider.GetService<IConfiguration>()!.GetSection("Tor");
        torExe = torConfig.GetValue<string>("Path", torExe);

        var pi = new ProcessStartInfo(torExe);
        pi.Arguments = $"--hush -f {Path.GetFullPath(torrc)}";
        pi.WorkingDirectory = runtimeTmp;
        pi.UseShellExecute = torConfig.GetValue<bool>("UseShellExecute", false);
        using var p = Process.Start(pi)!;
        try
        {
            {
                using var processCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                processCts.CancelAfter(torConfig.GetValue<int>("startupDelay", 500));
                try
                {
                    await p.WaitForExitAsync(processCts.Token);
                }
                catch (OperationCanceledException)
                {
                    if (p.HasExited) throw;
                }
            }

            var cookieAuthFile = torrcConfig.CookieAuthFile ?? "cookie-auth";
            if (!File.Exists(torrcConfig.CookieAuthFile))
            {
                cookieAuthFile = Path.Combine(runtimeTmp, cookieAuthFile);
            }

            var cookieAuth = await File.ReadAllBytesAsync(cookieAuthFile, cts.Token);


            var torClient = new TorControlClient(
                new ControlClientOptions()
                    { Port = torrcConfig.ControlPort.Value, Host = "localhost", Password = cookieAuth },
                serviceProvider.GetService<ILoggerFactory>()!.CreateLogger<TorControlClient>());

            await torClient.ChangeExitNodes(nodes.Take(5).Select(info => info.ExitNode), cts.Token);
        }
        finally
        {
            if (!p.HasExited)
            {
                p.Kill(true);
            }
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
                var httpClientFactoryHelper = provider.GetService<IHttpClientFactoryHelper>();
                httpClientFactoryHelper?.Configure(client);
            })
            .ConfigurePrimaryHttpMessageHandler(provider =>
                provider.GetService<IHttpClientFactoryHelper>()!.GetHandler(provider.GetService<ITorServiceSingleton>()
                    ?.WebProxy))
            .AddPolicyHandler((provider, _) => provider.GetService<IHttpClientFactoryHelper>()?.GetRetryPolicy())
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

    private static IConfigurationRoot BuidConfiguration()
    {
        var configurationRoot = new ConfigurationBuilder()
            .AddEnvironmentVariables("FRESHONION_")
            .AddJsonFile("appsettings.json", true)
            .Build();
        return configurationRoot;
    }
}