using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/slots")]
public class SlotsController : ControllerBase
{
    private readonly ISlotsLN _slotsLn;

    public SlotsController(ISlotsLN slotsLn) => _slotsLn = slotsLn;

    [HttpPost("spin")]
    public async Task<IActionResult> Spin([FromBody] TSlotsSpinRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _slotsLn.SpinAsync(userId, request.Bet);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
