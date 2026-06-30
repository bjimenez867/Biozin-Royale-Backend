using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/promotions")]
public class PromotionController : ControllerBase
{
    private readonly IPromotionLN _promotionLN;

    public PromotionController(IPromotionLN promotionLN)
    {
        _promotionLN = promotionLN;
    }

    // ──────────── Jugador ────────────

    [HttpGet]
    public async Task<IActionResult> GetActivas()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var res = await _promotionLN.ObtenerActivasAsync(userId);
        return res.blnError ? BadRequest(res) : Ok(res);
    }

    [HttpPost("{id:guid}/claim")]
    public async Task<IActionResult> Claim(Guid id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var res = await _promotionLN.ReclamarAsync(userId, id);
        return res.blnError ? BadRequest(res) : Ok(res);
    }

    [HttpGet("my")]
    public async Task<IActionResult> GetMyClaims()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var res = await _promotionLN.ObtenerMisReclamosAsync(userId);
        return res.blnError ? BadRequest(res) : Ok(res);
    }

    // ──────────── Admin ────────────

    [HttpGet("admin/all")]
    public async Task<IActionResult> AdminGetAll()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var res = await _promotionLN.ObtenerTodasAsync(userId);
        return res.blnError ? Forbid() : Ok(res);
    }

    [HttpPost("admin")]
    public async Task<IActionResult> AdminCreate([FromBody] TCreatePromotion datos)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var res = await _promotionLN.CrearPromocionAsync(userId, datos);
        return res.blnError ? BadRequest(res) : Ok(res);
    }

    [HttpPut("admin/{id:guid}/toggle")]
    public async Task<IActionResult> AdminToggle(Guid id)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var res = await _promotionLN.ToggleActivoAsync(userId, id);
        return res.blnError ? BadRequest(res) : Ok(res);
    }

    [HttpPost("admin/grant/{userId:guid}")]
    public async Task<IActionResult> AdminGrant(Guid userId, [FromBody] TCreatePromotion datos)
    {
        if (!TryGetUserId(out var adminId)) return Unauthorized();
        var res = await _promotionLN.OtorgarBonoAsync(adminId, userId, datos);
        return res.blnError ? BadRequest(res) : Ok(res);
    }

    [HttpGet("admin/users")]
    public async Task<IActionResult> AdminGetUsers()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var res = await _promotionLN.ObtenerUsuariosAsync(userId);
        return res.blnError ? Forbid() : Ok(res);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
