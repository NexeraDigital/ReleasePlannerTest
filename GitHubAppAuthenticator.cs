using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GitHubProjectConnection;

/// <summary>
/// Authenticates as a GitHub App: builds a short-lived RS256 JWT signed with the
/// app's private key, then exchanges it for a 1-hour installation access token.
/// See: https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app
/// </summary>
public sealed class GitHubAppAuthenticator(HttpClient http, string clientIdOrAppId, string privateKeyPem)
{
    /// <summary>
    /// Generates the App JWT. Per GitHub docs: RS256, iss = client/app id,
    /// iat set 60s in the past for clock drift, exp no more than 10 minutes out.
    /// </summary>
    public string CreateJwt()
    {
        // 'iat' is backdated 60 seconds to tolerate clock drift between us and GitHub.
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long iat = now - 60;
        long exp = now + (9 * 60); // 9 minutes, safely under the 10-minute ceiling.

        string header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "RS256",
            typ = "JWT"
        }));

        string payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iat,
            exp,
            iss = clientIdOrAppId
        }));

        string signingInput = $"{header}.{payload}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem); // Handles PKCS#1 and PKCS#8 PEM keys.
        byte[] signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64Url(signature)}";
    }

    /// <summary>
    /// Looks up the installation id for an org or user that has the app installed.
    /// Uses GET /orgs/{org}/installation or GET /users/{user}/installation, and if that
    /// 404s, falls back to GET /app/installations so we can report where it IS installed.
    /// </summary>
    public async Task<long> GetInstallationIdAsync(string jwt, string owner, bool isOrganization)
    {
        string path = isOrganization
            ? $"orgs/{owner}/installation"
            : $"users/{owner}/installation";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAppHeaders(request, jwt);

        using var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("id").GetInt64();
        }

        // Not installed on that owner — enumerate every account the App is installed on
        // so the error is actionable instead of a bare 404.
        var installs = await ListInstallationsAsync(jwt);
        string where = installs.Count == 0
            ? "This App is not installed on ANY account yet. Open the App's settings -> Install App."
            : "This App is currently installed on: " + string.Join(", ",
                installs.Select(i => $"{i.account} ({i.type}, id {i.id})"));

        throw new InvalidOperationException(
            $"No installation found for {(isOrganization ? "organization" : "user")} '{owner}'.\n{where}\n" +
            "Set Target:Owner / Target:OwnerType to one of the above (or install the App on that owner).");
    }

    /// <summary>Lists every account this App is installed on (account login, type, id).</summary>
    public async Task<List<(string account, string type, long id)>> ListInstallationsAsync(string jwt)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "app/installations");
        ApplyAppHeaders(request, jwt);

        using var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response, "list installations");

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
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
    public async Task<string> CreateInstallationTokenAsync(string jwt, long installationId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"app/installations/{installationId}/access_tokens");
        ApplyAppHeaders(request, jwt);

        using var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response, "create installation token");

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("Installation token missing from response.");
    }

    private static void ApplyAppHeaders(HttpRequestMessage request, string jwt)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            $"Failed to {what}: {(int)response.StatusCode} {response.ReasonPhrase}\n{detail}");
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
