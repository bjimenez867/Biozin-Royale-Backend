using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Biozin_Royale_Backend.Dominio.InterfacesLN;

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

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
