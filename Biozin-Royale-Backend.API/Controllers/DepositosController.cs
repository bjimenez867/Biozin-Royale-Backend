using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/depositos")]
public class DepositosController : ControllerBase
{
    private readonly IDepositosLN _depositosLN;

    public DepositosController(IDepositosLN depositosLN)
    {
        _depositosLN = depositosLN;
    }

    [HttpPost("stripe/iniciar")]
    public async Task<IActionResult> IniciarStripe([FromBody] TDepositoRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var resultado = await _depositosLN.IniciarStripeAsync(userId, request.Amount);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPost("paypal/iniciar")]
    public async Task<IActionResult> IniciarPayPal([FromBody] TDepositoRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var resultado = await _depositosLN.IniciarPayPalAsync(userId, request.Amount);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPost("paypal/capturar")]
    public async Task<IActionResult> CapturarPayPal([FromBody] TCapturarPayPalRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var resultado = await _depositosLN.CapturarPayPalAsync(userId, request.OrderId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
