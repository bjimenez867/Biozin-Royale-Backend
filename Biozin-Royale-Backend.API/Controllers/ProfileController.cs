using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/profile")]
public class ProfileController : ControllerBase
{
    private readonly IAuthLN _authLN;

    public ProfileController(IAuthLN authLN)
    {
        _authLN = authLN;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _authLN.ObtenerPerfilAsync(userId);
        return resultado.blnError ? NotFound(resultado) : Ok(resultado);
    }

    /// Completa los datos que el login social no entrega (teléfono, país, fecha de
    /// nacimiento) o los que el registro manual no pidió (username).
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] TActualizarPerfil datos)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _authLN.ActualizarPerfilAsync(userId, datos);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _authLN.ObtenerEstadisticasAsync(userId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}