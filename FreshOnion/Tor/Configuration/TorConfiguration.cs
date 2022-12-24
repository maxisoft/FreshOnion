namespace FreshOnion;

public class TorConfiguration
{
    
    public const int DefaultSocksPort = 9050;
    public int SocksPort { get; set; } = DefaultSocksPort;
    public List<string> ExitNodes { get; set; } = new();


    public string? CacheDirectory { get; set; } = "cache";
    public int? ControlPort { get; set; }
    public string? ControlPortWriteToFile { get; set; } = "cp.port";
    public string? CookieAuthFile { get; set; } = "cookie-auth";
    public string? DataDirectory { get; set; } = "data";
    public string? PidFile { get; set; }
    
    public string? TorrcFile { get; set; }
    
    public int? HTTPTunnelPort { get; set; }
    
    public int? EnforceDistinctSubnets { get; set; }

    public int NumExitNodes { get; set; } = 64;
}