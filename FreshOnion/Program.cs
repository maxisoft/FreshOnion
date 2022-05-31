// See https://aka.ms/new-console-template for more information

using FreshOnion;
using FreshOnion.IoC;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

class Program
{
    public static async Task Main(string[] args)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var serviceProvider = new ServiceCollection()
            .AddLogging(builder => { builder.AddSystemdConsole(); })
            .AddHttpClient<IExitListService, ExitListService>((provider, client) =>
            {
                var httpClientFactoryHelper = provider.GetService<IHttpClientFactoryHelper>();
                httpClientFactoryHelper?.Configure(client);
            })
            .Services
            .AddOptions()
            .BuildServiceProvider();


        var cts = new CancellationTokenSource();

        var logger = serviceProvider.GetService<ILoggerFactory>()!.CreateLogger<Program>();
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
    }
}