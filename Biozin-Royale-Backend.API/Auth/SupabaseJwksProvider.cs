using Microsoft.IdentityModel.Tokens;

namespace Biozin_Royale_Backend.API.Auth;

/// Descarga y cachea las claves públicas (JWKS) que Supabase usa para firmar
/// los JWT de sesión (login con Google y, más adelante, cualquier otro proveedor OAuth).
public class SupabaseJwksProvider
{
    private static readonly HttpClient Http = new();
    private readonly string _jwksUrl;
    private readonly TimeSpan _ttl = TimeSpan.FromHours(1);

    private IReadOnlyList<SecurityKey> _cache = Array.Empty<SecurityKey>();
    private DateTime _expiraEn = DateTime.MinValue;
    private readonly object _lock = new();

    public SupabaseJwksProvider(string supabaseUrl)
    {
        _jwksUrl = $"{supabaseUrl}/auth/v1/.well-known/jwks.json";
    }

    public IEnumerable<SecurityKey> GetSigningKeys()
    {
        lock (_lock)
        {
            if (DateTime.UtcNow < _expiraEn)
                return _cache;
        }

        var json = Http.GetStringAsync(_jwksUrl).GetAwaiter().GetResult();
        var keySet = new JsonWebKeySet(json);
        var claves = keySet.GetSigningKeys();

        lock (_lock)
        {
            _cache = claves.ToList();
            _expiraEn = DateTime.UtcNow.Add(_ttl);
            return _cache;
        }
    }
}
