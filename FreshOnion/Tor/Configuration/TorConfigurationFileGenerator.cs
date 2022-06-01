using FreshOnion.FileSystem;
using Stubble.Core.Builders;

namespace FreshOnion;

/// <summary>
/// Generate torrc file using mustache pattern
/// </summary>
public class TorConfigurationFileGenerator : ITorConfigurationFileGenerator
{
    private readonly IFileSearch _fileSearch;

    public TorConfigurationFileGenerator(IFileSearch fileSearch)
    {
        _fileSearch = fileSearch;
    }
    public async ValueTask<string> Generate(TorConfiguration configuration, CancellationToken cancellationToken)
    {
        var file = _fileSearch.GetFile("torrc.template");
        var template = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        var stubble = new StubbleBuilder().Configure(builder => builder.SetIgnoreCaseOnKeyLookup(true)).Build();
        return await stubble.RenderAsync(template, new
        {
            configuration.SocksPort,
            ExitNodes = configuration.ExitNodes.Select(node => new { Node = node }),
            configuration.CacheDirectory,
            configuration.ControlPort,
            configuration.ControlPortWriteToFile,
            configuration.CookieAuthFile,
            configuration.DataDirectory,
            configuration.PidFile,
            configuration.HTTPTunnelPort,
            configuration.EnforceDistinctSubnets
        }).ConfigureAwait(false);
    }
}