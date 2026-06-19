using System.Collections.Concurrent;

namespace Biozin_Royale_Backend.Utilidades;

/// <summary>
/// Almacén en memoria de códigos de recuperación de contraseña.
/// Cada entrada expira a los 15 minutos.
/// </summary>
public class RecoveryCode
{
    private static readonly ConcurrentDictionary<string, (string Codigo, DateTime Expira)> _store = new();

    public static string Generate(string email)
    {
        var code = new Random().Next(100000, 999999).ToString();
        _store[email.ToLower()] = (code, DateTime.UtcNow.AddMinutes(15));
        return code;
    }

    public static bool Validate(string email, string code)
    {
        var key = email.ToLower();
        if (!_store.TryGetValue(key, out var entry)) return false;
        if (DateTime.UtcNow > entry.Expira) { _store.TryRemove(key, out _); return false; }
        if (entry.Codigo != code) return false;
        _store.TryRemove(key, out _);
        return true;
    }
}
