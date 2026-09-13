using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Maxkeys.Api.Auth;

/// <summary>
/// Caches Supabase's JWKS document for 10 minutes (design section 6e) and
/// forces a fresh fetch once whenever an incoming token's <c>kid</c> is not
/// present in the cached set, so a key rotation on Supabase's side does not
/// require waiting out the full cache window (auth spec "Configurable JWT
/// Validation Mode"). Registered as a singleton so the cache state survives
/// across requests; a new <see cref="HttpClient"/> is created per fetch via
/// <see cref="IHttpClientFactory"/>, which is the recommended usage for that
/// factory (connections/handlers are pooled, not the logical client).
/// </summary>
public sealed class JwksKeyCache
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AuthOptions> _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private IReadOnlyList<JsonWebKey> _keys = [];
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public JwksKeyCache(IHttpClientFactory httpClientFactory, IOptionsMonitor<AuthOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    /// <summary>
    /// Returns the cached signing keys, refreshed if the cache expired. If
    /// <paramref name="kid"/> is not <see langword="null"/> and is not present
    /// in the (possibly stale) cached set, forces one fresh fetch regardless of
    /// the cache's remaining lifetime before returning.
    /// </summary>
    public async Task<IReadOnlyList<SecurityKey>> GetKeysForKidAsync(string? kid, CancellationToken cancellationToken = default)
    {
        var keys = await GetCachedKeysAsync(cancellationToken);

        if (kid is not null && keys.All(key => key.Kid != kid))
        {
            keys = await RefreshAsync(cancellationToken);
        }

        return keys;
    }

    private async Task<IReadOnlyList<JsonWebKey>> GetCachedKeysAsync(CancellationToken cancellationToken) =>
        DateTimeOffset.UtcNow < _expiresAt ? _keys : await RefreshAsync(cancellationToken);

    private async Task<IReadOnlyList<JsonWebKey>> RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            using var httpClient = _httpClientFactory.CreateClient(nameof(JwksKeyCache));
            var json = await httpClient.GetStringAsync(_options.CurrentValue.JwksUrl, cancellationToken);

            _keys = new JsonWebKeySet(json).Keys.ToList();
            _expiresAt = DateTimeOffset.UtcNow.Add(CacheDuration);

            return _keys;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
