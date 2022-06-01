namespace FreshOnion;

public class TorConfiguration
{
    public int SocksPort { get; set; } = 9050;
    public IEnumerable<string> ExitNodes { get; set; } = ArraySegment<string>.Empty;

    
    public string? CacheDirectory { get; set; }
    public int? ControlPort { get; set; }
    public string? ControlPortWriteToFile { get; set; }
    public string? CookieAuthFile { get; set; }
    public string? DataDirectory { get; set; }
    public string? PidFile { get; set; }
    
    public string? TorrcFile { get; set; }
    
    public int? HTTPTunnelPort { get; set; }
    
    public int? EnforceDistinctSubnets { get; set; }
}