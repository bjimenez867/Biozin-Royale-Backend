using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/ruleta")]
public class RuletaController : ControllerBase
{
    private readonly IRuletaLN _ruletaLn;

    public RuletaController(IRuletaLN ruletaLn) => _ruletaLn = ruletaLn;

    [HttpPost("spin")]
    public async Task<IActionResult> Spin([FromBody] TRuletaSpinRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _ruletaLn.SpinAsync(userId, request.Bets);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
