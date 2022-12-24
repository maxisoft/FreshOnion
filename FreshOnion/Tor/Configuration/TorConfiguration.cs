namespace FreshOnion;

public class TorConfiguration
{
    
    public const int DefaultSocksPort = 9050;
    public int SocksPort { get; set; } = DefaultSocksPort;
    public List<string> ExitNodes { get; set; } = new();

    
    public string? CacheDirectory { get; set; }
    public int? ControlPort { get; set; }
    public string? ControlPortWriteToFile { get; set; }
    public string? CookieAuthFile { get; set; }
    public string? DataDirectory { get; set; }
    public string? PidFile { get; set; }
    
    public string? TorrcFile { get; set; }
    
    public int? HTTPTunnelPort { get; set; }
    
    public int? EnforceDistinctSubnets { get; set; }

    public int NumExitNodes { get; set; } = 64;
}