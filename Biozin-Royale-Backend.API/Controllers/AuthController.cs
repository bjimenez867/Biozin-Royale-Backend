using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;

namespace Biozin_Royale_Backend.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthLN _authLN;

    public AuthController(IAuthLN authLN)
    {
        _authLN = authLN;
    }

    /// Registro manual (no usa Supabase Auth): crea la cuenta directamente en este backend.
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] TRegistroManual datos)
    {
        var resultado = await _authLN.RegistrarManualAsync(datos);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    /// Login directo con email + contraseña de una cuenta registrada manualmente.
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] TLoginManual datos)
    {
        var resultado = await _authLN.LoginManualAsync(datos.Email, datos.Password);
        return resultado.blnError ? Unauthorized(resultado) : Ok(resultado);
    }

    /// Llamado por el frontend justo después de que Supabase confirma una sesión OAuth
    /// (Google, y cualquier otro proveedor que se agregue después, como Facebook).
    [Authorize]
    [HttpPost("sync")]
    public async Task<IActionResult> Sync()
    {
        var sub = User.FindFirst("sub")?.Value;
        var email = User.FindFirst("email")?.Value;
        if (sub is null || email is null || !Guid.TryParse(sub, out var supabaseUserId))
            return Unauthorized();

        var nombreCompleto = ExtraerNombreCompleto(User.FindFirst("user_metadata")?.Value);

        var resultado = await _authLN.SincronizarOAuthAsync(supabaseUserId, email, nombreCompleto);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    private static string? ExtraerNombreCompleto(string? userMetadataJson)
    {
        if (string.IsNullOrWhiteSpace(userMetadataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(userMetadataJson);
            if (doc.RootElement.TryGetProperty("full_name", out var fullName))
                return fullName.GetString();
            if (doc.RootElement.TryGetProperty("name", out var name))
                return name.GetString();
        }
        catch (JsonException)
        {
            // user_metadata no vino como JSON parseable; se ignora y se deja sin nombre.
        }
        return null;
    }
}
