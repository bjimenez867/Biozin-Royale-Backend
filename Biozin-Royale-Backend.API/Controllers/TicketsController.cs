using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using System.Security.Claims;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/tickets")]
public class TicketsController : ControllerBase
{
    private readonly ITicketsLN _ticketsLN;

    public TicketsController(ITicketsLN ticketsLN)
    {
        _ticketsLN = ticketsLN;
    }

    // ── Tickets ────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] TCrearTicket datos)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var role = GetRole();
        if (role == "admin" || role == "soporte") return Forbid();

        var resultado = await _ticketsLN.CrearTicketAsync(datos, userId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var role = GetRole();
        if (role == "soporte" || role == "admin")
        {
            var r = await _ticketsLN.ListarTodosAsync();
            return r.blnError ? BadRequest(r) : Ok(r);
        }
        else
        {
            var r = await _ticketsLN.ListarTicketsUsuarioAsync(userId);
            return r.blnError ? BadRequest(r) : Ok(r);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obtener(Guid id)
    {
        if (!TryGetUserId(out var callerId)) return Unauthorized();

        var role = GetRole();
        var resultado = await _ticketsLN.ObtenerTicketAsync(id, callerId, role);
        return resultado.blnError ? NotFound(resultado) : Ok(resultado);
    }

    // ── Mensajes ───────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> ListarMensajes(Guid id)
    {
        if (!TryGetUserId(out var callerId)) return Unauthorized();

        var role = GetRole();
        var resultado = await _ticketsLN.ListarMensajesAsync(id, callerId, role);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPost("{id:guid}/messages")]
    public async Task<IActionResult> EnviarMensaje(Guid id, [FromBody] TEnviarMensaje datos)
    {
        if (!TryGetUserId(out var senderId)) return Unauthorized();

        var role = GetRole();
        var resultado = await _ticketsLN.EnviarMensajeAsync(id, senderId, role, datos);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    // ── Gestión (solo staff) ───────────────────────────────────────────────

    [HttpPatch("{id:guid}/assign")]
    public async Task<IActionResult> Asignar(Guid id, [FromBody] TAsignarTicket datos)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        if (!IsStaff()) return Forbid();

        var resultado = await _ticketsLN.AsignarTicketAsync(id, datos.StaffMemberId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> CambiarEstado(Guid id, [FromBody] TCambiarEstado datos)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        if (!IsStaff()) return Forbid();

        var resultado = await _ticketsLN.CambiarEstadoAsync(id, datos.Status);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpGet("agents")]
    public async Task<IActionResult> ListarAgentes()
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        if (!IsStaff()) return Forbid();

        var resultado = await _ticketsLN.ListarAgentesAsync();
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }

    private string GetRole() => User.FindFirst("role")?.Value ?? "user";

    private bool IsStaff()
    {
        var role = GetRole();
        return role == "admin" || role == "soporte";
    }
}
