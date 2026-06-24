using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitHubProjectConnection.Errors;
using GitHubProjectConnection.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubProjectConnection.Auth;

/// <summary>An installation access token together with its server-provided expiry.</summary>
public sealed record InstallationToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Authenticates as a GitHub App: builds a short-lived RS256 JWT signed with the app's
/// private key, then exchanges it for a ~1-hour installation access token.
/// See: https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app
/// </summary>
public sealed class GitHubAppAuthenticator
{
    private readonly HttpClient _http;
    private readonly GitHubAppOptions _options;
    private readonly ILogger<GitHubAppAuthenticator> _logger;
    private readonly Lazy<string> _privateKeyPem;

    public GitHubAppAuthenticator(
        HttpClient http,
        IOptions<GitHubAppOptions> options,
        ILogger<GitHubAppAuthenticator> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _privateKeyPem = new Lazy<string>(() => ResolvePrivateKeyPem(_options));
    }

    /// <summary>
    /// Generates the App JWT. Per GitHub docs: RS256, <c>iss</c> = client/app id,
    /// <c>iat</c> set 60s in the past for clock drift, <c>exp</c> no more than 10 minutes out.
    /// </summary>
    public string CreateJwt()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long iat = now - 60;          // backdated 60s to tolerate clock drift vs. GitHub.
        long exp = now + (9 * 60);    // 9 minutes, safely under the 10-minute ceiling.

        string header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        string payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { iat, exp, iss = _options.ClientIdOrAppId }));
        string signingInput = $"{header}.{payload}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(_privateKeyPem.Value); // Handles PKCS#1 and PKCS#8 PEM keys.
        byte[] signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64Url(signature)}";
    }

    /// <summary>
    /// Looks up the installation id for an org or user that has the app installed. If that
    /// 404s, enumerates every account the App IS installed on so the error is actionable.
    /// </summary>
    public async Task<long> GetInstallationIdAsync(
        string jwt, string owner, bool isOrganization, CancellationToken cancellationToken)
    {
        string path = isOrganization ? $"orgs/{owner}/installation" : $"users/{owner}/installation";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAppHeaders(request, jwt);

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return body.GetProperty("id").GetInt64();
        }

        // Not installed on that owner — list everywhere it IS installed instead of a bare 404.
        IReadOnlyList<(string account, string type, long id)> installs = await ListInstallationsAsync(jwt, cancellationToken);
        string where = installs.Count == 0
            ? "This App is not installed on ANY account yet. Open the App's settings -> Install App."
            : "This App is currently installed on: " +
              string.Join(", ", installs.Select(i => $"{i.account} ({i.type}, id {i.id})"));

        throw new GitHubApiException(
            $"No installation found for {(isOrganization ? "organization" : "user")} '{owner}'.\n{where}\n" +
            "Set Target:Owner / Target:OwnerType to one of the above (or install the App on that owner).",
            response.StatusCode);
    }

    /// <summary>Lists every account this App is installed on (account login, type, id).</summary>
    public async Task<IReadOnlyList<(string account, string type, long id)>> ListInstallationsAsync(
        string jwt, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "app/installations");
        ApplyAppHeaders(request, jwt);

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "list installations", cancellationToken);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var result = new List<(string, string, long)>();
        foreach (JsonElement install in body.EnumerateArray())
        {
            JsonElement account = install.GetProperty("account");
            result.Add((
                account.GetProperty("login").GetString() ?? "?",
                account.GetProperty("type").GetString() ?? "?",
                install.GetProperty("id").GetInt64()));
        }
        return result;
    }

    /// <summary>
    /// Exchanges the App JWT for an installation access token (valid ~1 hour).
    /// POST /app/installations/{installation_id}/access_tokens
    /// </summary>
    public async Task<InstallationToken> CreateInstallationTokenAsync(
        string jwt, long installationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"app/installations/{installationId}/access_tokens");
        ApplyAppHeaders(request, jwt);

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "create installation token", cancellationToken);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        string token = body.GetProperty("token").GetString()
            ?? throw new GitHubApiException("Installation token missing from response.");
        DateTimeOffset expiresAt = body.TryGetProperty("expires_at", out JsonElement exp) &&
                                   exp.TryGetDateTimeOffset(out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.UtcNow.AddHours(1); // documented default lifetime.

        _logger.LogDebug("Minted installation token for installation {InstallationId}, expires {ExpiresAt:u}.",
            installationId, expiresAt);
        return new InstallationToken(token, expiresAt);
    }

    private static void ApplyAppHeaders(HttpRequestMessage request, string jwt)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw GitHubApiException.FromRest(operation, response.StatusCode, response.ReasonPhrase, body);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Resolves the PEM from the inline option, or from the file path (probing common roots).</summary>
    private static string ResolvePrivateKeyPem(GitHubAppOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PrivateKeyPem))
            return options.PrivateKeyPem;

        if (string.IsNullOrWhiteSpace(options.PrivateKeyPath))
            throw new InvalidOperationException(
                "No private key configured. Set GitHubApp:PrivateKeyPath or GitHubApp:PrivateKeyPem.");

        string path = options.PrivateKeyPath;
        if (Path.IsPathRooted(path)) return File.ReadAllText(path);

        foreach (string root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            string candidate = Path.Combine(root, path);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException(
            $"Could not find private key '{path}'. Place it next to appsettings.json or set " +
            "GitHubApp:PrivateKeyPath to an absolute path (or supply GitHubApp:PrivateKeyPem).");
    }
}
