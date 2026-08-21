using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace UsfmScannerNet;

/// <summary>
/// Adds Basic auth credentials to outgoing requests based on the request host.
/// Credentials come from the "Gitea" configuration section, keyed by host name.
/// </summary>
public class AuthInjector: DelegatingHandler
{
    private readonly Dictionary<string, AuthConfiguration> _authConfigurations;
    private readonly bool _allowInsecureAuth;

    public AuthInjector(IConfiguration configuration)
    {
        _allowInsecureAuth = configuration.GetValue("AllowInsecureAuth", false);
        _authConfigurations = new Dictionary<string, AuthConfiguration>(
            configuration.GetSection("Gitea").Get<Dictionary<string, AuthConfiguration>>() ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host;
        var scheme = request.RequestUri?.Scheme;
        if (host != null && (_allowInsecureAuth || scheme == "https") && _authConfigurations.TryGetValue(host, out var authConfig))
        {
            var byteArray = Encoding.UTF8.GetBytes($"{authConfig.User}:{authConfig.Password}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        }
        return base.SendAsync(request, cancellationToken);
    }
}

internal class AuthConfiguration
{
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}