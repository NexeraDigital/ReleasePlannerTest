using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitHubProjectConnection.Auth;
using GitHubProjectConnection.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GitHubProjectConnection.Tests;

public class GitHubAppJwtTests
{
    private static (string jwt, RSA rsa) CreateJwt(string clientId)
    {
        var rsa = RSA.Create(2048);
        var options = new GitHubAppOptions
        {
            ClientIdOrAppId = clientId,
            PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem()
        };

        var authenticator = new GitHubAppAuthenticator(
            new HttpClient(),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<GitHubAppAuthenticator>.Instance);

        return (authenticator.CreateJwt(), rsa);
    }

    private static JsonElement DecodeSegment(string segment)
    {
        string padded = segment.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch { 2 => padded + "==", 3 => padded + "=", _ => padded };
        return JsonSerializer.Deserialize<JsonElement>(Convert.FromBase64String(padded));
    }

    [Fact]
    public void Jwt_has_three_segments()
    {
        (string jwt, _) = CreateJwt("client-123");
        Assert.Equal(3, jwt.Split('.').Length);
    }

    [Fact]
    public void Header_uses_rs256()
    {
        (string jwt, _) = CreateJwt("client-123");
        JsonElement header = DecodeSegment(jwt.Split('.')[0]);
        Assert.Equal("RS256", header.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.GetProperty("typ").GetString());
    }

    [Fact]
    public void Payload_issuer_is_client_id_with_valid_time_window()
    {
        (string jwt, _) = CreateJwt("client-123");
        JsonElement payload = DecodeSegment(jwt.Split('.')[1]);

        Assert.Equal("client-123", payload.GetProperty("iss").GetString());

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long iat = payload.GetProperty("iat").GetInt64();
        long exp = payload.GetProperty("exp").GetInt64();

        Assert.True(iat <= now, "iat should be backdated for clock drift");
        Assert.True(iat >= now - 120, "iat should be close to now");
        Assert.True(exp > now, "exp should be in the future");
        Assert.True(exp - now <= 600, "exp must be no more than 10 minutes out");
    }

    [Fact]
    public void Signature_verifies_against_the_signing_key()
    {
        (string jwt, RSA rsa) = CreateJwt("client-123");
        string[] parts = jwt.Split('.');

        byte[] signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        byte[] signature = DecodeSegmentBytes(parts[2]);

        bool valid = rsa.VerifyData(
            signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert.True(valid);
    }

    private static byte[] DecodeSegmentBytes(string segment)
    {
        string padded = segment.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch { 2 => padded + "==", 3 => padded + "=", _ => padded };
        return Convert.FromBase64String(padded);
    }
}
