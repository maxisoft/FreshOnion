using System.Diagnostics;

namespace FreshOnion.Tor;

public interface ITorServiceSingleton : ITorService
{
    public Process? Process { get; }
    
    public IReadOnlySet<string> Nodes { get; }
    public bool AddNode(string node);
    public bool RemoveNode(string node);
}