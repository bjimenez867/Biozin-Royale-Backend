using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using System.Security.Claims;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/staff")]
public class StaffController : ControllerBase
{
    private readonly IStaffLN _staffLN;

    public StaffController(IStaffLN staffLN)
    {
        _staffLN = staffLN;
    }

    // ── Gestión de equipo (solo admin) ────────────────────────────

    [HttpGet]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Listar()
    {
        var resultado = await _staffLN.ListarMiembrosAsync();
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Crear([FromBody] TCrearStaffMember datos)
    {
        if (!TryGetUserId(out var creadoPorId)) return Unauthorized();

        var resultado = await _staffLN.CrearMiembroAsync(datos, creadoPorId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    // ── Gestión individual de miembro (solo admin) ────────────────

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ObtenerMiembro(Guid id)
    {
        var resultado = await _staffLN.ObtenerMiembroAsync(id);
        return resultado.blnError ? NotFound(resultado) : Ok(resultado);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ActualizarMiembro(Guid id, [FromBody] TActualizarStaffMember datos)
    {
        var resultado = await _staffLN.ActualizarMiembroAsync(id, datos);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> CambiarStatus(Guid id, [FromBody] TCambiarStatusStaffMember datos)
    {
        var resultado = await _staffLN.CambiarStatusMiembroAsync(id, datos.Status);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> RestablecerPassword(Guid id)
    {
        if (!TryGetUserId(out var callerId) || callerId == id)
            return BadRequest(new { blnError = true, strResponseTittle = "Acción no permitida", strResponseMessage = "No puedes restablecer tu propia contraseña desde aquí." });

        var resultado = await _staffLN.RestablecerPasswordStaffAsync(id);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> EliminarMiembro(Guid id)
    {
        if (!TryGetUserId(out var callerId) || callerId == id)
            return BadRequest(new { blnError = true, strResponseTittle = "Acción no permitida", strResponseMessage = "No puedes eliminarte a ti mismo." });

        var resultado = await _staffLN.EliminarMiembroAsync(id);
        return resultado.blnError ? NotFound(resultado) : Ok(resultado);
    }

    // ── Perfil propio (admin y soporte) ───────────────────────────

    [HttpGet("me")]
    [Authorize(Roles = "admin,soporte")]
    public async Task<IActionResult> ObtenerMe()
    {
        if (!TryGetUserId(out var staffId)) return Unauthorized();

        var resultado = await _staffLN.ObtenerMeAsync(staffId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPut("me")]
    [Authorize(Roles = "admin,soporte")]
    public async Task<IActionResult> ActualizarMe([FromBody] TActualizarStaffMember datos)
    {
        if (!TryGetUserId(out var staffId)) return Unauthorized();

        var resultado = await _staffLN.ActualizarMeAsync(staffId, datos);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPut("me/password")]
    [Authorize(Roles = "admin,soporte")]
    public async Task<IActionResult> CambiarPassword([FromBody] TCambiarPasswordStaff datos)
    {
        if (!TryGetUserId(out var staffId)) return Unauthorized();

        var resultado = await _staffLN.CambiarPasswordAsync(staffId, datos.OldPassword, datos.NewPassword);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    // ── Sesiones activas (admin y soporte) ─────────────────────────

    [HttpGet("sessions")]
    [Authorize(Roles = "admin,soporte")]
    public async Task<IActionResult> ObtenerSesiones()
    {
        if (!TryGetUserId(out var staffId)) return Unauthorized();

        TryGetSessionId(out var currentSessionId);
        var resultado = await _staffLN.ObtenerSesionesAsync(staffId, currentSessionId);
        return resultado.blnError ? NotFound(resultado) : Ok(resultado);
    }

    [HttpDelete("sessions/{id:guid}")]
    [Authorize(Roles = "admin,soporte")]
    public async Task<IActionResult> CerrarSesion(Guid id)
    {
        if (!TryGetUserId(out var staffId)) return Unauthorized();

        var resultado = await _staffLN.CerrarSesionAsync(staffId, id);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPost("sessions/close-others")]
    [Authorize(Roles = "admin,soporte")]
    public async Task<IActionResult> CerrarOtrasSesiones()
    {
        if (!TryGetUserId(out var staffId)) return Unauthorized();
        if (!TryGetSessionId(out var currentSessionId)) return Unauthorized();

        var resultado = await _staffLN.CerrarOtrasSesionesAsync(staffId, currentSessionId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    // Llamado por el frontend en AuthService.logout() para staff/soporte, igual que
    // ProfileController.CerrarSesionActual() para usuarios normales.
    [HttpPost("sessions/logout")]
    [Authorize(Roles = "admin,soporte")]
    public async Task<IActionResult> CerrarSesionActual()
    {
        if (!TryGetUserId(out var staffId)) return Unauthorized();
        if (!TryGetSessionId(out var currentSessionId)) return Unauthorized();

        var resultado = await _staffLN.CerrarSesionAsync(staffId, currentSessionId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpGet("security-history")]
    [Authorize(Roles = "admin,soporte")]
    public async Task<IActionResult> ObtenerHistorialSeguridad()
    {
        if (!TryGetUserId(out var staffId)) return Unauthorized();

        var resultado = await _staffLN.ObtenerHistorialSeguridadAsync(staffId);
        return resultado.blnError ? NotFound(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }

    // Tokens de staff traen "jti" (ver StaffLN.GenerarTokenConSesion), igual que los
    // de login manual de usuario — no hay ruta OAuth/Supabase para staff.
    private bool TryGetSessionId(out Guid sessionId)
    {
        var jti = User.FindFirst("jti")?.Value;
        return Guid.TryParse(jti, out sessionId);
    }
}
