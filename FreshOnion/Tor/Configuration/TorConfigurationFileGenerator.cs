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

    private static string? ResolvePath(string? s, string directory)
    {
        if (s is null)
        {
            return null;
        }

        var absolute = Path.GetFullPath(s);
        return s == absolute ? absolute : Path.Combine(directory, s);
    }
    
    public async ValueTask<string> Generate(TorConfiguration configuration, string workingDirectory, CancellationToken cancellationToken)
    {
        var file = _fileSearch.GetFile("torrc.template");
        var template = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
        var stubble = new StubbleBuilder().Configure(builder => builder.SetIgnoreCaseOnKeyLookup(true)).Build();

        string? Resolve(string? s)
        {
            return ResolvePath(s, workingDirectory);
        }
        
        return await stubble.RenderAsync(template, new
        {
            configuration.SocksPort,
            ExitNodes = configuration.ExitNodes.Select(node => new { Node = node }),
            CacheDirectory = Resolve(configuration.CacheDirectory),
            configuration.ControlPort,
            ControlPortWriteToFile = Resolve(configuration.ControlPortWriteToFile),
            CookieAuthFile = Resolve(configuration.CookieAuthFile),
            DataDirectory = Resolve(configuration.DataDirectory),
            PidFile = Resolve(configuration.PidFile),
            configuration.HTTPTunnelPort,
            configuration.EnforceDistinctSubnets,
            HardwareAccel = 1
        }).ConfigureAwait(false);
    }
}