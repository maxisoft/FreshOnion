using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace FreshOnion.FileSystem;

public interface IFileSearch
{
    string GetFile(string name);
}

public class FileSearch : IFileSearch
{
    private readonly IConfiguration _configuration;

    public FileSearch(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private static IEnumerable<string> AdditionDirectories()
    {
        yield return Directory.GetCurrentDirectory();
        var assemblyDirectory = Directory.GetParent(Assembly.GetCallingAssembly().Location)?.FullName ?? string.Empty;
        if (!string.IsNullOrEmpty(assemblyDirectory))
        {
            yield return assemblyDirectory;
        }
        
    }
    
    private static readonly List<string> EmptyList = new List<string>();

    public string GetFile(string name)
    {
        var paths = _configuration.GetValue<List<string>>("SearchPath", EmptyList);
        
        foreach (var path in paths.Concat(AdditionDirectories()))
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var p = Path.Combine(path, name);
            if (File.Exists(p))
            {
                return p;
            }
        }

        return name;
    }
}