using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/gameshistory")]
public class GamesHistoryController : ControllerBase
{
    private readonly IGamesHistoryLN _gamesHistoryLn;

    public GamesHistoryController(IGamesHistoryLN gamesHistoryLn)
    {
        _gamesHistoryLn = gamesHistoryLn;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _gamesHistoryLn.ObtenerHistorialAsync(userId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}