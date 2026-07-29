using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/metodos-pago")]
public class MetodosPagoController : ControllerBase
{
    private readonly IMetodosPagoLN _metodosPagoLN;

    public MetodosPagoController(IMetodosPagoLN metodosPagoLN)
    {
        _metodosPagoLN = metodosPagoLN;
    }

    private Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpGet]
    public IActionResult Listar() =>
        Ok(_metodosPagoLN.Listar(UserId));

    [HttpPost("paypal")]
    public IActionResult AgregarPayPal([FromBody] TAgregarPayPalRequest request) =>
        Ok(_metodosPagoLN.AgregarPayPal(UserId, request));

    [HttpPost("tarjeta")]
    public IActionResult AgregarTarjeta([FromBody] TAgregarTarjetaRequest request) =>
        Ok(_metodosPagoLN.AgregarTarjeta(UserId, request));

    [HttpDelete("{id:guid}")]
    public IActionResult Eliminar(Guid id) =>
        Ok(_metodosPagoLN.Eliminar(UserId, id));

    [HttpPut("{id:guid}/principal")]
    public IActionResult EstablecerPrincipal(Guid id) =>
        Ok(_metodosPagoLN.EstablecerPrincipal(UserId, id));
}
