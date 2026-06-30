using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize(Roles = "admin")]
[ApiController]
[Route("api/staff")]
public class StaffController : ControllerBase
{
    private readonly IStaffLN _staffLN;

    public StaffController(IStaffLN staffLN)
    {
        _staffLN = staffLN;
    }

    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        var resultado = await _staffLN.ListarMiembrosAsync();
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPost]
    public async Task<IActionResult> Crear([FromBody] TCrearStaffMember datos)
    {
        if (!TryGetUserId(out var creadoPorId)) return Unauthorized();

        var resultado = await _staffLN.CrearMiembroAsync(datos, creadoPorId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}