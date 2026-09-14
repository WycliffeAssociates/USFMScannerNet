using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UsfmScannerNet.Tests;

public class AuthInjectorTests
{
    private const string Host = "git.example.org";
    private const string User = "usfm-scanner";
    private const string Password = "gitea-access-token";

    // Scheme gating: credentials belong on TLS only, unless explicitly opted out of.

    [Fact]
    public async Task HttpsRequestToConfiguredHost_SendsBasicCredentials()
    {
        var inner = await SendAsync($"https://{Host}/api/v1/repos/user/repo/archive/master.zip", GiteaConfig());

        Assert.Equal("Basic", inner.Authorization?.Scheme);
        Assert.Equal($"{User}:{Password}", DecodeBasic(inner.Authorization));
    }

    [Fact]
    public async Task HttpRequestToConfiguredHost_WithholdsCredentials()
    {
        var inner = await SendAsync($"http://{Host}/api/v1/repos/user/repo/archive/master.zip", GiteaConfig());

        Assert.Null(inner.Authorization);
    }

    [Fact]
    public async Task HttpRequest_WhenInsecureAuthAllowed_SendsCredentials()
    {
        var inner = await SendAsync($"http://{Host}/x", GiteaConfig(allowInsecureAuth: "true"));

        Assert.Equal($"{User}:{Password}", DecodeBasic(inner.Authorization));
    }

    [Fact]
    public async Task HttpRequest_WhenInsecureAuthExplicitlyDisabled_WithholdsCredentials()
    {
        var inner = await SendAsync($"http://{Host}/x", GiteaConfig(allowInsecureAuth: "false"));

        Assert.Null(inner.Authorization);
    }

    [Fact]
    public void BlankAllowInsecureAuthValue_FailsWhenTheHandlerChainIsBuilt()
    {
        // Documents a rough edge: GetValue<bool> cannot convert "", so a deployment that passes
        // AllowInsecureAuth through as an empty value (for example an explicit compose entry
        // "- AllowInsecureAuth=${AllowInsecureAuth}" with the variable unset) fails when the client
        // is created, which is during ScannerService construction. Reading the flag as a string, or
        // as bool?, would make an empty value mean "off" instead. Update this test if that changes.
        var services = new ServiceCollection();
        services.AddSingleton(GiteaConfig(allowInsecureAuth: ""));
        services.AddTransient<AuthInjector>();
        services.AddHttpClient("http").AddHttpMessageHandler<AuthInjector>();
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient("http"));
        Assert.Contains("AllowInsecureAuth", exception.Message);
    }

    [Fact]
    public async Task UppercaseHttpsScheme_SendsCredentials()
    {
        // Uri normalizes the scheme to lower case, so the == "https" comparison still matches.
        var inner = await SendAsync($"HTTPS://{Host.ToUpperInvariant()}/x", GiteaConfig());

        Assert.Equal($"{User}:{Password}", DecodeBasic(inner.Authorization));
    }

    // Host matching.

    // Uri lower-cases the host itself, so the OrdinalIgnoreCase comparer is what makes a
    // configured key like "Content.Example.Org" match a request to content.example.org.
    [Theory]
    [InlineData("git.example.org", "git.example.org")]
    [InlineData("GIT.EXAMPLE.ORG", "git.example.org")]
    [InlineData("git.example.org", "GIT.EXAMPLE.ORG")]
    public async Task HostMatching_IgnoresCase(string configuredHost, string requestHost)
    {
        var inner = await SendAsync($"https://{requestHost}/x", GiteaConfig(host: configuredHost));

        Assert.Equal($"{User}:{Password}", DecodeBasic(inner.Authorization));
    }

    [Fact]
    public async Task UnconfiguredHost_SendsNoCredentials()
    {
        var inner = await SendAsync("https://other.example.org/x", GiteaConfig());

        Assert.Null(inner.Authorization);
    }

    [Fact]
    public async Task SubdomainOfConfiguredHost_SendsNoCredentials()
    {
        // Matching is exact, not by suffix, so a look-alike host cannot harvest the credentials.
        var inner = await SendAsync($"https://sub.{Host}/x", GiteaConfig());

        Assert.Null(inner.Authorization);
    }

    [Fact]
    public async Task NonDefaultPortOnConfiguredHost_SendsCredentials()
    {
        // Credentials are keyed by host alone, so a Gitea instance on a custom port still matches.
        var inner = await SendAsync($"https://{Host}:8443/x", GiteaConfig());

        Assert.Equal($"{User}:{Password}", DecodeBasic(inner.Authorization));
    }

    [Fact]
    public async Task MultipleConfiguredHosts_EachSendsItsOwnCredentials()
    {
        var configuration = Config(new Dictionary<string, string?>
        {
            ["Gitea:first.example.org:User"] = "first-user",
            ["Gitea:first.example.org:Password"] = "first-pass",
            ["Gitea:second.example.org:User"] = "second-user",
            ["Gitea:second.example.org:Password"] = "second-pass",
        });

        var first = await SendAsync("https://first.example.org/x", configuration);
        var second = await SendAsync("https://second.example.org/x", configuration);

        Assert.Equal("first-user:first-pass", DecodeBasic(first.Authorization));
        Assert.Equal("second-user:second-pass", DecodeBasic(second.Authorization));
    }

    // Configuration shapes seen in real deployments.

    [Fact]
    public async Task MissingGiteaSection_SendsNoCredentials()
    {
        var inner = await SendAsync($"https://{Host}/x", Config(new Dictionary<string, string?>()));

        Assert.Null(inner.Authorization);
    }

    [Fact]
    public async Task EmptyHostKey_SendsNoCredentials()
    {
        // docker-compose interpolates Gitea__${GiteaHost}__User down to Gitea____User when the
        // deploy host leaves the credential variables unset. That must stay anonymous, not crash.
        var inner = await SendAsync($"https://{Host}/x", Config(new Dictionary<string, string?>
        {
            ["Gitea::User"] = "",
            ["Gitea::Password"] = "",
        }));

        Assert.Null(inner.Authorization);
    }

    [Fact]
    public async Task EnvironmentVariableConfiguration_BindsHostContainingDots()
    {
        // Container config arrives as Gitea__git.example.org__User; the provider maps __ to :.
        // Key names are unique to this test so it stays safe under parallel execution.
        const string envHost = "env-test.example.org";
        var userKey = $"Gitea__{envHost}__User";
        var passwordKey = $"Gitea__{envHost}__Password";
        try
        {
            Environment.SetEnvironmentVariable(userKey, User);
            Environment.SetEnvironmentVariable(passwordKey, Password);

            var inner = await SendAsync($"https://{envHost}/x",
                new ConfigurationBuilder().AddEnvironmentVariables().Build());

            Assert.Equal($"{User}:{Password}", DecodeBasic(inner.Authorization));
        }
        finally
        {
            Environment.SetEnvironmentVariable(userKey, null);
            Environment.SetEnvironmentVariable(passwordKey, null);
        }
    }

    [Fact]
    public async Task BlankCredentialsForConfiguredHost_StillSendsEmptyBasicAuth()
    {
        // Documents current behaviour: a host configured with blank values sends "Basic OjA=" style
        // empty credentials, so Gitea answers 401 instead of the request falling back to anonymous.
        // If AuthInjector gains a guard for empty user/password, flip this assertion to Assert.Null.
        var inner = await SendAsync($"https://{Host}/x", GiteaConfig(user: "", password: ""));

        Assert.Equal(":", DecodeBasic(inner.Authorization));
    }

    [Fact]
    public async Task Credentials_AreUtf8EncodedBeforeBase64()
    {
        var inner = await SendAsync($"https://{Host}/x", GiteaConfig(user: "ünïcode", password: "pässwörd"));

        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("ünïcode:pässwörd")), inner.Authorization?.Parameter);
    }

    // Pass-through behaviour.

    [Fact]
    public async Task Handler_ForwardsToInnerHandlerAndReturnsItsResponse()
    {
        var inner = new CapturingHandler { ResponseStatus = HttpStatusCode.NotFound };
        using var client = new HttpClient(new AuthInjector(GiteaConfig()) { InnerHandler = inner });

        using var response = await client.GetAsync($"https://{Host}/missing");

        Assert.Equal(1, inner.Calls);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnconfiguredHost_LeavesACallerSuppliedAuthorizationHeaderIntact()
    {
        var inner = new CapturingHandler();
        using var client = new HttpClient(new AuthInjector(GiteaConfig()) { InnerHandler = inner });
        var request = new HttpRequestMessage(HttpMethod.Get, "https://other.example.org/x")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", "caller-token") }
        };

        using var response = await client.SendAsync(request);

        Assert.Equal("Bearer", inner.Authorization?.Scheme);
        Assert.Equal("caller-token", inner.Authorization?.Parameter);
    }

    [Fact]
    public async Task NamedHttpClient_RunsHandlerAndKeepsConfiguredUserAgent()
    {
        // Mirrors the registration in Program.cs: the handler only participates for the named
        // "http" client, which is also the client that carries the User-Agent header.
        var inner = new CapturingHandler();
        var services = new ServiceCollection();
        services.AddSingleton(GiteaConfig());
        services.AddTransient<AuthInjector>();
        services.AddHttpClient("http", c => c.DefaultRequestHeaders.Add("User-Agent", "ScriptureRenderingPipeline"))
            .AddHttpMessageHandler<AuthInjector>()
            .ConfigurePrimaryHttpMessageHandler(() => inner);

        var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("http");
        using var response = await client.GetAsync($"https://{Host}/x");

        Assert.Equal($"{User}:{Password}", DecodeBasic(inner.Authorization));
        Assert.Equal("ScriptureRenderingPipeline", inner.UserAgent);
    }

    private static async Task<CapturingHandler> SendAsync(string url, IConfiguration configuration)
    {
        var inner = new CapturingHandler();
        using var client = new HttpClient(new AuthInjector(configuration) { InnerHandler = inner });
        using var response = await client.GetAsync(url);
        return inner;
    }

    private static IConfiguration GiteaConfig(string host = Host, string user = User, string password = Password,
        string? allowInsecureAuth = null)
    {
        var values = new Dictionary<string, string?>
        {
            [$"Gitea:{host}:User"] = user,
            [$"Gitea:{host}:Password"] = password,
        };
        if (allowInsecureAuth != null)
        {
            values["AllowInsecureAuth"] = allowInsecureAuth;
        }
        return Config(values);
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string? DecodeBasic(AuthenticationHeaderValue? header) =>
        header?.Parameter == null ? null : Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpStatusCode ResponseStatus { get; init; } = HttpStatusCode.OK;
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? UserAgent { get; private set; }
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Authorization = request.Headers.Authorization;
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(ResponseStatus));
        }
    }
}
