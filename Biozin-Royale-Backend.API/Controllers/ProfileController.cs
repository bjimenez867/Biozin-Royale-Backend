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
    private readonly IProfileLN _profileLN;

    public ProfileController(IProfileLN profileLN)
    {
        _profileLN = profileLN;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _profileLN.ObtenerPerfilAsync(userId);
        return resultado.blnError ? NotFound(resultado) : Ok(resultado);
    }

    /// Completa los datos que el login social no entrega (teléfono, país, fecha de
    /// nacimiento) o los que el registro manual no pidió (username).
    [HttpPut]
    public async Task<IActionResult> Update([FromBody] TActualizarPerfil datos)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _profileLN.ActualizarPerfilAsync(userId, datos);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _profileLN.ObtenerEstadisticasAsync(userId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPut("password")]
    public async Task<IActionResult> CambiarPassword([FromBody] TCambiarPassword datos)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _profileLN.CambiarPasswordAsync(userId, datos.OldPassword, datos.NewPassword);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPost("pin")]
    public async Task<IActionResult> CrearPin([FromBody] TCrearPin datos)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _profileLN.CrearPinAsync(userId, datos.Pin);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPut("pin")]
    public async Task<IActionResult> CambiarPin([FromBody] TCambiarPin datos)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _profileLN.CambiarPinAsync(userId, datos.OldPin, datos.NewPin);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPut("pin/estado")]
    public async Task<IActionResult> CambiarEstadoPin([FromBody] TCambiarEstadoPin datos)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _profileLN.CambiarEstadoPinAsync(userId, datos.Pin, datos.Enabled);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPut("2fa/estado")]
    public async Task<IActionResult> CambiarEstadoTwoFactor([FromBody] TCambiarEstadoTwoFactor datos)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _profileLN.CambiarEstadoTwoFactorAsync(userId, datos.Password, datos.Enabled);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}