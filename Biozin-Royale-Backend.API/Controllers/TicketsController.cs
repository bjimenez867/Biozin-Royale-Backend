using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Biozin_Royale_Backend.API.Hubs;
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
    private readonly IHubContext<ChatHub> _chatHub;

    public TicketsController(ITicketsLN ticketsLN, IHubContext<ChatHub> chatHub)
    {
        _ticketsLN = ticketsLN;
        _chatHub = chatHub;
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
        if (resultado.blnError) return BadRequest(resultado);

        // Tiempo real: el otro extremo del chat lo recibe al instante (el emisor
        // deduplica por id, ya que también está en el grupo)
        await _chatHub.Clients.Group(ChatHub.TicketGroup(id))
            .SendAsync("ticketMensaje", new { ticketId = id, mensaje = resultado.ReturnValue });

        return Ok(resultado);
    }

    // ── Gestión (solo staff) ───────────────────────────────────────────────

    [HttpPatch("{id:guid}/assign")]
    public async Task<IActionResult> Asignar(Guid id, [FromBody] TAsignarTicket datos)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        if (!IsStaff()) return Forbid();

        var resultado = await _ticketsLN.AsignarTicketAsync(id, datos.StaffMemberId);
        if (resultado.blnError) return BadRequest(resultado);

        await NotificarTicketAsync(id, resultado.ReturnValue!);
        return Ok(resultado);
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> CambiarEstado(Guid id, [FromBody] TCambiarEstado datos)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        if (!IsStaff()) return Forbid();

        var resultado = await _ticketsLN.CambiarEstadoAsync(id, datos.Status);
        if (resultado.blnError) return BadRequest(resultado);

        await NotificarTicketAsync(id, resultado.ReturnValue!);
        return Ok(resultado);
    }

    // ── Acciones del usuario ───────────────────────────────────────────────

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reabrir(Guid id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (IsStaff()) return Forbid();

        var resultado = await _ticketsLN.ReopenAsync(id, userId);
        if (resultado.blnError) return BadRequest(resultado);

        await NotificarTicketAsync(id, resultado.ReturnValue!);
        return Ok(resultado);
    }

    [HttpPost("{id:guid}/rate")]
    public async Task<IActionResult> Valorar(Guid id, [FromBody] TRateTicket datos)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (IsStaff()) return Forbid();

        var resultado = await _ticketsLN.RateAsync(id, userId, datos.Rating);
        if (resultado.blnError) return BadRequest(resultado);

        await NotificarTicketAsync(id, resultado.ReturnValue!);
        return Ok(resultado);
    }

    [HttpPost("{id:guid}/cerrar")]
    public async Task<IActionResult> Cerrar(Guid id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (IsStaff()) return Forbid();

        var resultado = await _ticketsLN.CerrarAsync(id, userId);
        if (resultado.blnError) return BadRequest(resultado);

        await NotificarTicketAsync(id, resultado.ReturnValue!);
        return Ok(resultado);
    }

    [HttpGet("agents")]
    public async Task<IActionResult> ListarAgentes()
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        if (!IsStaff()) return Forbid();

        var resultado = await _ticketsLN.ListarAgentesAsync();
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    // ── Notificaciones (solo staff) ─────────────────────────────────────────

    [HttpGet("notifications")]
    public async Task<IActionResult> ObtenerNotificaciones([FromQuery] DateTime? since)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        if (!IsStaff()) return Forbid();

        // El binder de query string puede parsear el sufijo "Z" del ISO string del cliente
        // convirtiéndolo a hora local del servidor (Kind=Local); si solo re-etiquetáramos
        // el Kind sin convertir, la ventana de "nuevo" quedaría corrida por el offset de
        // zona horaria del servidor. Se normaliza explícito a UTC en cada caso.
        var sinceUtc = since switch
        {
            null => DateTime.UtcNow,
            { Kind: DateTimeKind.Utc } dt => dt,
            { Kind: DateTimeKind.Local } dt => dt.ToUniversalTime(),
            { } dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        };

        var resultado = await _ticketsLN.ObtenerNotificacionesAsync(sinceUtc);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// Cambios de estado/asignación/valoración: ambos lados del chat ven el
    /// ticket actualizado al instante sin recargar.
    private Task NotificarTicketAsync(Guid id, object ticket) =>
        _chatHub.Clients.Group(ChatHub.TicketGroup(id)).SendAsync("ticketActualizado", ticket);

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
