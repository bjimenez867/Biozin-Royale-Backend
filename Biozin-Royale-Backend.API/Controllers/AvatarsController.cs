using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Biozin_Royale_Backend.Dominio.InterfacesLN;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/avatars")]
public class AvatarsController : ControllerBase
{
    private readonly IAvatarLN _avatarLN;

    public AvatarsController(IAvatarLN avatarLN)
    {
        _avatarLN = avatarLN;
    }

    [HttpGet]
    public IActionResult Listar()
    {
        var resultado = _avatarLN.ListarAvatars();
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(int id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var resultado = await _avatarLN.ActualizarAvatarAsync(userId, id);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
