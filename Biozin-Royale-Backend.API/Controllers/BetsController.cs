using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/bets")]
public class BetsController : ControllerBase
{
    private readonly IBetsLN _betsLn;

    public BetsController(IBetsLN betsLn)
    {
        _betsLn = betsLn;
    }

    [HttpPost]
    public async Task<IActionResult> PlaceBet([FromBody] TPlaceBetRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _betsLn.PlaceBetAsync(userId, request);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
