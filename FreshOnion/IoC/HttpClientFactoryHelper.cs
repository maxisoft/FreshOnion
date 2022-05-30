using System.Net;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Extensions.Http;

namespace FreshOnion.IoC;

public interface IHttpClientFactoryHelper
{
    void Configure(HttpClient client);
    IAsyncPolicy<HttpResponseMessage> GetRetryPolicy();
    HttpClientHandler GetHandler();
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
            _httpConfig.GetValue<string>("UserAgent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.4 Safari/605.1.15");
        if (!string.IsNullOrEmpty(userAgent))
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }
    }

    public HttpClientHandler GetHandler()
    {
        var handler = new HttpClientHandler
            { MaxConnectionsPerServer = _httpConfig.GetValue("MaxConnectionsPerServer", 16) };
        var proxyString = _httpConfig.GetValue("Proxy", string.Empty);
        if (Uri.TryCreate(proxyString, UriKind.Absolute, out var proxyUri))
        {
            var proxy = new WebProxy
            {
                Address = proxyUri
            };
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
                    retryAttempt)) * _httpConfig.GetValue("RetryMs", 100));
    }
}