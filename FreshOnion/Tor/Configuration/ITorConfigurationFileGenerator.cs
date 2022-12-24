namespace FreshOnion;

public interface ITorConfigurationFileGenerator
{
    ValueTask<string> Generate(TorConfiguration configuration, string workingDirectory, CancellationToken cancellationToken);
}