using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreshOnion.IoC;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FreshOnion.Tests;

public class ExitListServiceTests
{
    private readonly ServiceProvider _serviceProvider;

    public ExitListServiceTests()
    {
        _serviceProvider = new ServiceCollection()
            .AddLogging(builder => { builder.AddSystemdConsole(); })
            .AddHttpClient<IExitListService, ExitListService>()
            .Services
            .AddOptions()
            .BuildServiceProvider();
    }
    
    [Fact]
    public async Task TestGetExitListsUrls()
    {
        var service = _serviceProvider.GetService<IExitListService>();
        Assert.NotNull(service);
        using var cts = new CancellationTokenSource(30_000);
        var urls = await service!.GetExitListsUrls(cts.Token);
        Assert.NotEmpty(urls);
        foreach (var (url, date) in urls)
        {
            Assert.True(Uri.IsWellFormedUriString(url, UriKind.Absolute));
        }

        var hs = urls.Select((url, _) => url).ToHashSet();
        Assert.Equal(hs.Count, urls.Count);
    }
    
    [Fact]
    public async Task TestGetExitNodeInfos()
    {
        var service = _serviceProvider.GetService<IExitListService>();
        Assert.NotNull(service);
        using var cts = new CancellationTokenSource(30_000);
        var urls = await service!.GetExitListsUrls(cts.Token);

        var nodes = await service.GetExitNodeInfos(new Uri(urls[^1].url), cts.Token);
        Assert.NotEmpty(nodes);
    }
}