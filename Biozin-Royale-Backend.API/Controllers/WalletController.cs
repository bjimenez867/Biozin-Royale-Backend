using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize]
[ApiController]
[Route("api/wallet")]
public class WalletController : ControllerBase
{
    private readonly IWalletLN _walletLn;

    public WalletController(IWalletLN walletLn)
    {
        _walletLn = walletLn;
    }

    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _walletLn.GetBalanceAsync(userId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpPut("balance")]
    public async Task<IActionResult> UpdateBalance([FromBody] decimal newBalance)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _walletLn.UpdateBalanceAsync(userId, newBalance);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
