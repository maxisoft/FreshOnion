using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using FreshOnion.Tor.ControlPort;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FreshOnion.Tor;

public interface ITorService : IDisposable
{
    /// <summary>
    /// Set tor's exit nodes to given nodes and rebuild circuits.
    /// </summary>
    /// <param name="nodes"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The previous Exit Nodes if any</returns>
    Task<string> ChangeExitNodes(IEnumerable<string> nodes, CancellationToken cancellationToken);

    /// <summary>
    /// Start a tor instance
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task Start(CancellationToken cancellationToken);

    WebProxy WebProxy { get; }

    string Id { get; }
}

public class TorService : ITorService
{
    protected readonly IConfiguration _configuration;
    protected readonly ILogger _logger;
    protected readonly IConfigurationSection _freshOnionConfiguration;
    protected readonly string _tmpDirectory;
    protected static readonly Random _random = new Random();
    protected readonly IServiceProvider _container;
    protected readonly ITorConfigurationFileGenerator _torConfigurationFileGenerator;

    public TorService(IServiceProvider container, IConfiguration configuration, ILoggerFactory loggerFactory,
        ITorConfigurationFileGenerator torConfigurationFileGenerator)
    {
        _configuration = configuration;
        _freshOnionConfiguration = configuration.GetSection("FreshOnion");
        _logger = loggerFactory.CreateLogger(GetType());
        var tmp = Path.GetTempPath();
        _tmpDirectory = _configuration.GetValue<string>("TempPath", tmp);
        _container = container;
        _torConfigurationFileGenerator = torConfigurationFileGenerator;
    }

    public TorConfiguration TorConfiguration { get; private set; } = new();
    public Process? Process { get; private set; }

    public Guid Guid { get; private set; } = Guid.Empty;

    public string WorkingDirectory => Path.Combine(_tmpDirectory, "FreshOnion", Guid.ToString());

    /// <summary>
    /// Set tor's exit nodes to given nodes and rebuild circuits.
    /// </summary>
    /// <param name="nodes"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>The previous Exit Nodes if any</returns>
    public virtual async Task<string> ChangeExitNodes(IEnumerable<string> nodes, CancellationToken cancellationToken)
    {
        var cookieAuthFile = TorConfiguration.CookieAuthFile ?? "";
        var cookieAuth = Array.Empty<byte>();
        if (!string.IsNullOrWhiteSpace(cookieAuthFile) && File.Exists(cookieAuthFile))
        {
            cookieAuth = await File.ReadAllBytesAsync(cookieAuthFile, cancellationToken).ConfigureAwait(false);
        }

        var client = new TorControlClient(
            new ControlClientOptions()
                { Port = TorConfiguration.ControlPort!.Value, Host = "localhost", Password = cookieAuth },
            _container.GetService<ILoggerFactory>()!.CreateLogger<TorControlClient>());
        using var clientToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        clientToken.CancelAfter(_freshOnionConfiguration.GetValue<int>("ControlPortTimeout", 5000));
        return await client.ChangeExitNodes(nodes, cancellationToken).ConfigureAwait(false);
    }

    public async Task Start(CancellationToken cancellationToken)
    {
        if (Process is { HasExited: false }) throw new Exception();
        Guid = Guid.NewGuid();
        TorConfiguration = await SetupDirectory(cancellationToken).ConfigureAwait(false);
        Process = await StartTor(cancellationToken).ConfigureAwait(false);
    }

    public WebProxy WebProxy => new($"socks4a://127.0.0.1:{TorConfiguration?.SocksPort ?? 9050}");
    public virtual string Id => Guid.ToString();

    protected virtual TorConfiguration InitTorConfiguration(string wd)
    {
        var port = _random.Next(15000, 32000);
        var config = new TorConfiguration()
        {
            SocksPort = port,
            ControlPort = port + 1,
            CookieAuthFile = Path.Combine(wd, "cookie-auth"),
            EnforceDistinctSubnets = 0
        };
        if (!_freshOnionConfiguration.GetValue<bool>("UseCookieAuthFile", true))
        {
            config.CookieAuthFile = null;
        }

        config.TorrcFile = Path.Combine(wd, "torrc");

        return config;
    }

    private async Task<TorConfiguration> SetupDirectory(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var wd = WorkingDirectory;
        Directory.CreateDirectory(wd);
        var config = InitTorConfiguration(wd);
        var generatedConfig =
            await _torConfigurationFileGenerator.Generate(config, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(config.TorrcFile!, generatedConfig, cancellationToken).ConfigureAwait(false);
        return config;
    }

    private async Task<Process> StartTor(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var torExe = "tor";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            torExe += ".exe";
        }

        var torConfig = _configuration.GetSection("Tor");
        torExe = torConfig.GetValue<string>("Path", torExe);
        var pi = new ProcessStartInfo(torExe);
        var quiet = torConfig.GetValue<bool>("quiet", false);
        pi.Arguments = $"--hush -f {Path.GetFullPath(TorConfiguration.TorrcFile!)}";
        if (quiet)
        {
            pi.Arguments = pi.Arguments.Replace("--hust", "--quiet");
        }

        pi.WorkingDirectory = WorkingDirectory;
        pi.UseShellExecute = torConfig.GetValue<bool>("UseShellExecute", false);
        var p = Process.Start(pi)!;
        try
        {
            var startupDelay = torConfig.GetValue<int>("StartupDelay", 500);
            if (startupDelay > 0)
            {
                using var processCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                processCts.CancelAfter(startupDelay);
                try
                {
                    await p.WaitForExitAsync(processCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (p.HasExited) throw;
                }
            }
        }
        catch (Exception)
        {
            p.Dispose();
            throw;
        }

        return p;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Process is { HasExited: false })
        {
            Process.Kill(true);
        }

        var wd = WorkingDirectory;
        if (Directory.Exists(wd))
        {
            Directory.Delete(wd, true);
        }

        Process?.Dispose();
        Guid = Guid.Empty;
    }
}