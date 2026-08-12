using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Biozin_Royale_Backend.API.Hubs;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize(Roles = "admin,soporte")]
[ApiController]
[Route("api/internal-requests")]
public class InternalRequestsController : ControllerBase
{
    private readonly IInternalRequestsLN _internalRequestsLN;
    private readonly IHubContext<ChatHub> _chatHub;

    public InternalRequestsController(IInternalRequestsLN internalRequestsLN, IHubContext<ChatHub> chatHub)
    {
        _internalRequestsLN = internalRequestsLN;
        _chatHub = chatHub;
    }

    // ── Solicitudes ──────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] TCrearInternalRequest datos)
    {
        if (!TryGetUserId(out var soporteId)) return Unauthorized();
        if (GetRole() != "soporte") return Forbid();

        var resultado = await _internalRequestsLN.CrearAsync(datos, soporteId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        if (!TryGetUserId(out var callerId)) return Unauthorized();

        if (GetRole() == "admin")
        {
            var r = await _internalRequestsLN.ListarParaMiAsync();
            return r.blnError ? BadRequest(r) : Ok(r);
        }
        else
        {
            var r = await _internalRequestsLN.ListarMiasAsync(callerId);
            return r.blnError ? BadRequest(r) : Ok(r);
        }
    }

    [HttpGet("admins")]
    public async Task<IActionResult> ListarAdmins()
    {
        if (!TryGetUserId(out _)) return Unauthorized();

        var resultado = await _internalRequestsLN.ListarAdminsAsync();
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obtener(Guid id)
    {
        if (!TryGetUserId(out var callerId)) return Unauthorized();

        var resultado = await _internalRequestsLN.ObtenerAsync(id, callerId, GetRole());
        return resultado.blnError ? NotFound(resultado) : Ok(resultado);
    }

    // ── Mensajes ─────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> ListarMensajes(Guid id)
    {
        if (!TryGetUserId(out var callerId)) return Unauthorized();

        var resultado = await _internalRequestsLN.ListarMensajesAsync(id, callerId, GetRole());
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPost("{id:guid}/messages")]
    public async Task<IActionResult> EnviarMensaje(Guid id, [FromBody] TEnviarInternalRequestMensaje datos)
    {
        if (!TryGetUserId(out var senderId)) return Unauthorized();

        var resultado = await _internalRequestsLN.EnviarMensajeAsync(id, senderId, GetRole(), datos);
        if (resultado.blnError) return BadRequest(resultado);

        // Tiempo real: el otro extremo del chat lo recibe al instante (el emisor
        // deduplica por id, ya que también está en el grupo)
        await _chatHub.Clients.Group(ChatHub.SolicitudGroup(id))
            .SendAsync("solicitudMensaje", new { solicitudId = id, mensaje = resultado.ReturnValue });

        return Ok(resultado);
    }

    // ── Gestión (solo admin) ─────────────────────────────────────────────

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> CambiarEstado(Guid id, [FromBody] TCambiarEstadoInternalRequest datos)
    {
        if (!TryGetUserId(out _)) return Unauthorized();
        if (GetRole() != "admin") return Forbid();

        var resultado = await _internalRequestsLN.CambiarEstadoAsync(id, datos.Status);
        if (resultado.blnError) return BadRequest(resultado);

        await _chatHub.Clients.Group(ChatHub.SolicitudGroup(id))
            .SendAsync("solicitudActualizada", resultado.ReturnValue);

        return Ok(resultado);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }

    private string GetRole() => User.FindFirst("role")?.Value ?? "user";
}
