using GitHubProjectConnection.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubProjectConnection.Auth;

/// <summary>
/// Provides an installation access token, caching it until shortly before it expires so we
/// don't mint a fresh one on every call. Installation tokens last ~1 hour; this refreshes
/// once inside a safety window of that.
/// </summary>
public sealed class InstallationTokenProvider : IDisposable
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private readonly GitHubAppAuthenticator _authenticator;
    private readonly GitHubAppOptions _appOptions;
    private readonly TargetOptions _target;
    private readonly ILogger<InstallationTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private InstallationToken? _cached;

    public InstallationTokenProvider(
        GitHubAppAuthenticator authenticator,
        IOptions<GitHubAppOptions> appOptions,
        IOptions<TargetOptions> target,
        ILogger<InstallationTokenProvider> logger)
    {
        _authenticator = authenticator;
        _appOptions = appOptions.Value;
        _target = target.Value;
        _logger = logger;
    }

    /// <summary>Returns a valid installation token, refreshing it if absent or near expiry.</summary>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsFresh(_cached)) return _cached!.Token;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsFresh(_cached)) return _cached!.Token;
            _cached = await MintAsync(cancellationToken);
            return _cached.Token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<InstallationToken> MintAsync(CancellationToken cancellationToken)
    {
        string jwt = _authenticator.CreateJwt();

        long installationId = _appOptions.InstallationId is > 0
            ? _appOptions.InstallationId.Value
            : await _authenticator.GetInstallationIdAsync(
                jwt, _target.Owner, _target.IsOrganization, cancellationToken);

        _logger.LogInformation("Using installation id {InstallationId}.", installationId);
        InstallationToken token = await _authenticator.CreateInstallationTokenAsync(jwt, installationId, cancellationToken);
        _logger.LogInformation("Obtained installation access token (expires {ExpiresAt:u}).", token.ExpiresAt);
        return token;
    }

    private static bool IsFresh(InstallationToken? token) =>
        token is not null && DateTimeOffset.UtcNow < token.ExpiresAt - RefreshSkew;

    public void Dispose() => _gate.Dispose();
}
