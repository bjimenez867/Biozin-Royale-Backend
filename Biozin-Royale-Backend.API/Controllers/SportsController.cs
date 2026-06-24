using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Microsoft.AspNetCore.Mvc;

namespace Biozin_Royale_Backend.API.Controllers;

[ApiController]
[Route("api/sports")]
public class SportsController : ControllerBase
{
    private readonly ISportsLN _sportsLn;

    public SportsController(ISportsLN sportsLn)
    {
        _sportsLn = sportsLn;
    }

    [HttpGet("matches")]
    public async Task<IActionResult> GetMatches()
    {
        var resultado = await _sportsLn.GetMatchesAsync();
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }
}
