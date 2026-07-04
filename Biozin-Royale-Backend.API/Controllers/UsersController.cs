using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize(Roles = "admin")]
[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IProfileLN _profileLN;

    public UsersController(IProfileLN profileLN)
    {
        _profileLN = profileLN;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        if (!TryGetUserId(out var adminId)) return Unauthorized();
        var res = await _profileLN.ObtenerUsuariosAsync(adminId);
        return res.blnError ? Forbid() : Ok(res);
    }

    [HttpGet("{userId:guid}/block")]
    public async Task<IActionResult> GetBlock(Guid userId)
    {
        if (!TryGetUserId(out var adminId)) return Unauthorized();
        var res = await _profileLN.ObtenerBloqueoActivoAsync(adminId, userId);
        return res.blnError ? BadRequest(res) : Ok(res);
    }

    [HttpPost("{userId:guid}/block")]
    public async Task<IActionResult> Block(Guid userId, [FromBody] TBlockUserRequest datos)
    {
        if (!TryGetUserId(out var adminId)) return Unauthorized();
        var res = await _profileLN.BloquearUsuarioAsync(adminId, userId, datos);
        return res.blnError ? BadRequest(res) : Ok(res);
    }

    [HttpDelete("{userId:guid}/block")]
    public async Task<IActionResult> Unblock(Guid userId)
    {
        if (!TryGetUserId(out var adminId)) return Unauthorized();
        var res = await _profileLN.DesbloquearUsuarioAsync(adminId, userId);
        return res.blnError ? BadRequest(res) : Ok(res);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
