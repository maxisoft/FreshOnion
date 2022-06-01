namespace FreshOnion;

public interface ITorConfigurationFileGenerator
{
    ValueTask<string> Generate(TorConfiguration configuration, CancellationToken cancellationToken);
}