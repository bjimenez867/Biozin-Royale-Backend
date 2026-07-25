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

    // Usuarios (y OAuth "authenticated") crean sus tickets
    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] TCrearTicket datos)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        // Staff no debe crear tickets de soporte
        var role = User.FindFirst("role")?.Value ?? string.Empty;
        if (role == "admin" || role == "soporte")
            return Forbid();

        var resultado = await _ticketsLN.CrearTicketAsync(datos, userId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    // Usuario: sus tickets. Soporte/Admin: todos.
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var role = User.FindFirst("role")?.Value ?? string.Empty;

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

        var role = User.FindFirst("role")?.Value ?? "user";
        var resultado = await _ticketsLN.ObtenerTicketAsync(id, callerId, role);
        return resultado.blnError ? NotFound(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
