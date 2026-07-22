using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/retiros")]
public class RetirosController : ControllerBase
{
    private readonly IRetirosLN _retirosLN;

    public RetirosController(IRetirosLN retirosLN)
    {
        _retirosLN = retirosLN;
    }

    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpPost]
    public async Task<IActionResult> Retirar([FromBody] TIniciarRetiroRequest request) =>
        Ok(await _retirosLN.ProcesarRetiroAsync(UserId, request));
}
