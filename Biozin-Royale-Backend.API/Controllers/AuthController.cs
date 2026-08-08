using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] TRegistroManual datos)
    {
        var resultado = await _authLN.RegistrarManualAsync(datos);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    /// Login directo con email + contraseña de una cuenta registrada manualmente.
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] TLoginManual datos)
    {
        var (userAgent, ip) = ObtenerInfoCliente();
        var resultado = await _authLN.LoginManualAsync(datos.Email, datos.Password, userAgent, ip);
        return resultado.blnError ? Unauthorized(resultado) : Ok(resultado);
    }

    /// Llamado por el frontend justo después de que Supabase confirma una sesión OAuth
    /// (Google, y cualquier otro proveedor que se agregue después, como Facebook).
    [Authorize]
    [HttpPost("sync")]
    public async Task<IActionResult> Sync()
    {
        var sub = User.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var supabaseUserId))
            return Unauthorized();

        // Las sesiones anónimas de Supabase (invitado) traen el claim "is_anonymous"
        // y no traen "email" — a diferencia del login social normal, donde sí es obligatorio.
        var esAnonimo = User.FindFirst("is_anonymous")?.Value == "true";
        var email = User.FindFirst("email")?.Value;
        if (!esAnonimo && email is null)
            return Unauthorized();

        var nombreCompleto = ExtraerNombreCompleto(User.FindFirst("user_metadata")?.Value);

        Guid.TryParse(User.FindFirst("session_id")?.Value, out var sessionId);
        var (userAgent, ip) = ObtenerInfoCliente();

        var resultado = await _authLN.SincronizarOAuthAsync(
            supabaseUserId, email, nombreCompleto, esAnonimo,
            sessionId == Guid.Empty ? null : sessionId, userAgent, ip);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    /// Llamado cuando alguien que entró como invitado decide crear cuenta: convierte
    /// el mismo Profile (IsGuest=true) en una cuenta manual normal, en vez de crear
    /// una fila nueva y abandonar el saldo/progreso del invitado.
    [Authorize]
    [HttpPost("claim-guest")]
    public async Task<IActionResult> ClaimGuest([FromBody] TRegistroManual datos)
    {
        var sub = User.FindFirst("sub")?.Value;
        if (sub is null || !Guid.TryParse(sub, out var userId))
            return Unauthorized();

        var resultado = await _authLN.ReclamarInvitadoAsync(userId, datos);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] TSolicitarRecuperacion datos)
    {
        var resultado = await _authLN.SolicitarRecuperacionAsync(datos.Email);
        return Ok(resultado);
    }

    [EnableRateLimiting("auth-codes")]
    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] TRestablecerPassword datos)
    {
        var resultado = await _authLN.RestablecerPasswordAsync(datos.Email, datos.Code, datos.NewPassword);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [EnableRateLimiting("auth-codes")]
    [AllowAnonymous]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] TVerificarEmail datos)
    {
        var resultado = await _authLN.VerificarEmailAsync(datos.Email, datos.Code);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] TSolicitarRecuperacion datos)
    {
        var resultado = await _authLN.EnviarVerificacionAsync(datos.Email);
        return Ok(resultado);
    }

    [EnableRateLimiting("auth-codes")]
    [AllowAnonymous]
    [HttpPost("verify-2fa")]
    public async Task<IActionResult> VerifyTwoFactor([FromBody] TVerificarCodigo2FA datos)
    {
        var (userAgent, ip) = ObtenerInfoCliente();
        var resultado = await _authLN.VerificarCodigo2FAAsync(datos.Email, datos.Code, userAgent, ip);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    [HttpPost("resend-2fa")]
    public async Task<IActionResult> ResendTwoFactor([FromBody] TReenviarCodigo2FA datos)
    {
        var resultado = await _authLN.ReenviarCodigo2FAAsync(datos.Email);
        return Ok(resultado);
    }

    [EnableRateLimiting("auth")]
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] TRefreshToken datos)
    {
        var resultado = await _authLN.RefreshAsync(datos.RefreshToken);
        return resultado.blnError ? Unauthorized(resultado) : Ok(resultado);
    }

    private (string? userAgent, string? ip) ObtenerInfoCliente()
    {
        var userAgent = Request.Headers.UserAgent.ToString();
        var ip = Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
                 ?? HttpContext.Connection.RemoteIpAddress?.ToString();
        return (string.IsNullOrWhiteSpace(userAgent) ? null : userAgent, ip);
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
