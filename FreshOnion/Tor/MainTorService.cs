using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FreshOnion.Tor;

/// <summary>
/// The TorService used to expose best ExitNodes
/// </summary>
public class MainTorService : TorService, ITorServiceSingleton
{
    public MainTorService(IServiceProvider container, IConfiguration configuration, ILoggerFactory loggerFactory,
        ITorConfigurationFileGenerator torConfigurationFileGenerator) : base(container, configuration, loggerFactory,
        torConfigurationFileGenerator) { }

    public override string Id => "main";

    private readonly HashSet<string> _validNodes = new HashSet<string>();

    public IReadOnlySet<string> Nodes
    {
        get
        {
            lock (_validNodes)
            {
                return _validNodes.ToImmutableHashSet();
            }
        }
    }

    public bool AddNode(string node)
    {
        if (string.IsNullOrWhiteSpace(node)) return false;
        lock (_validNodes)
        {
            return _validNodes.Add(node);
        }
    }

    public bool RemoveNode(string node)
    {
        lock (_validNodes)
        {
            return _validNodes.Remove(node);
        }
    }

    protected override TorConfiguration InitTorConfiguration(string wd)
    {
        var config = base.InitTorConfiguration(wd);
        var torSection = _configuration.GetSection("Tor");
        config.SocksPort = torSection.GetValue<int>("SocksPort", 9050);
        config.ControlPort = torSection.GetValue<int>("ControlPort", config.SocksPort + 1);
        config.HTTPTunnelPort = torSection.GetValue<int>("HTTPTunnelPort", config.SocksPort + 2);
        config.EnforceDistinctSubnets = torSection.GetValue<int?>("EnforceDistinctSubnets", null);
        if (!torSection
                .GetValue<bool>("UseCookieAuthFile", !string.IsNullOrEmpty(config.CookieAuthFile)))
        {
            config.CookieAuthFile = null;
        }

        return config;
    }
}