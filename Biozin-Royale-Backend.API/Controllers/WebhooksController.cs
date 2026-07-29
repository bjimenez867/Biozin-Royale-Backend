using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Biozin_Royale_Backend.API.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IDepositosLN _depositosLN;

    public WebhooksController(IDepositosLN depositosLN)
    {
        _depositosLN = depositosLN;
    }

    [HttpPost("stripe")]
    public async Task<IActionResult> StripeWebhook()
    {
        var payload   = await new StreamReader(Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"].ToString();

        var resultado = await _depositosLN.ProcesarWebhookStripeAsync(payload, signature);
        return resultado.blnError ? BadRequest() : Ok();
    }
}
