using Microsoft.IdentityModel.Tokens;

namespace Biozin_Royale_Backend.API.Auth;

/// Cachea las claves públicas JWKS de Supabase.
/// GetSigningKeys() solo lee memoria; la red la toca únicamente JwksRefreshService (async).
public class SupabaseJwksProvider
{
    private static readonly HttpClient Http = new();
    private readonly string _jwksUrl;
    private readonly TimeSpan _ttl = TimeSpan.FromHours(1);

    private IReadOnlyList<SecurityKey> _cache = [];
    private long _expiraEnTicks = 0;
    private readonly SemaphoreSlim _sem = new(1, 1);

    public SupabaseJwksProvider(string supabaseUrl)
    {
        _jwksUrl = $"{supabaseUrl}/auth/v1/.well-known/jwks.json";
    }

    /// Llamado por IssuerSigningKeyResolver (síncrono por contrato del framework).
    /// Si el caché está caliente devuelve en ~0 ms.
    /// El único caso de bloqueo es el primer request justo antes de que
    /// JwksRefreshService complete su carga inicial — ocurre máximo una vez por arranque.
    public IEnumerable<SecurityKey> GetSigningKeys()
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _expiraEnTicks))
            return _cache;

        RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
        return _cache;
    }

    /// Llamado por JwksRefreshService de forma completamente async.
    public async Task RefreshAsync(CancellationToken ct)
    {
        // Fast-path: otro hilo ya refrescó mientras esperábamos.
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _expiraEnTicks)) return;

        await _sem.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _expiraEnTicks)) return;

            var json  = await Http.GetStringAsync(_jwksUrl, ct);
            var keys  = new JsonWebKeySet(json).GetSigningKeys().ToList();

            _cache = keys;
            Interlocked.Exchange(ref _expiraEnTicks, DateTime.UtcNow.Add(_ttl).Ticks);
        }
        finally
        {
            _sem.Release();
        }
    }
}
