using System.Net;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Extensions.Http;

namespace FreshOnion.IoC;

public interface IHttpClientFactoryHelper
{
    void Configure(HttpClient client);
    IAsyncPolicy<HttpResponseMessage> GetRetryPolicy();
    HttpClientHandler GetHandler(IWebProxy? proxy = null);
}

// ReSharper disable once UnusedType.Global
public class HttpClientFactoryHelper : IHttpClientFactoryHelper
{
    private readonly IConfigurationSection? _httpConfig;

    public HttpClientFactoryHelper(IConfiguration configuration)
    {
        _httpConfig = configuration.GetSection("Http");
    }

    public void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromMilliseconds(_httpConfig.GetValue("TimeoutMs", 15 * 1000));
        client.MaxResponseContentBufferSize = _httpConfig.GetValue<long>("MaxResponseContentBufferSize", 1 << 20);
        var userAgent =
            _httpConfig.GetValue<string>("UserAgent",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.4 Safari/605.1.15");
        if (!string.IsNullOrEmpty(userAgent))
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }
    }

    public HttpClientHandler GetHandler(IWebProxy? proxy = null)
    {
        var handler = new HttpClientHandler
            { MaxConnectionsPerServer = _httpConfig.GetValue("MaxConnectionsPerServer", 16) };
        
        if (proxy is null)
        {
            var proxyString = _httpConfig.GetValue("Proxy", string.Empty);
            if (Uri.TryCreate(proxyString, UriKind.Absolute, out var proxyUri))
            {
                proxy = new WebProxy
                {
                    Address = proxyUri
                };
            }
        }

        if (proxy is not null)
        {
            handler.Proxy = proxy;
        }
        
        return handler;
    }

    public IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.NotFound)
            .WaitAndRetryAsync(_httpConfig.GetValue("NumRetry", 2), retryAttempt =>
                TimeSpan.FromMilliseconds(Math.Pow(2,
                    retryAttempt)) * _httpConfig.GetValue("RetryMs", 2000));
    }
}