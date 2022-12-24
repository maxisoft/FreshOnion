using System.Diagnostics;

namespace FreshOnion.Tor;

public interface ITorServiceSingleton : ITorService
{
    public Process? Process { get; }

    public async ValueTask Restart(CancellationToken cancellationToken)
    {
        Dispose();
        await Start(cancellationToken);
    }
}