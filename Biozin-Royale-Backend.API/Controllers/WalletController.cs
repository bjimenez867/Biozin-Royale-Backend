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

    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions()
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var resultado = await _walletLn.GetTransactionsAsync(userId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [Authorize(Roles = "admin")]
    [HttpGet("admin/{userId:guid}/transactions")]
    public async Task<IActionResult> GetTransactionsByUser(Guid userId)
    {
        var resultado = await _walletLn.GetTransactionsAsync(userId);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [Authorize(Roles = "admin")]
    [HttpGet("admin/summary")]
    public async Task<IActionResult> GetAdminSummary()
    {
        var resultado = await _walletLn.GetAdminSummaryAsync();
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [Authorize(Roles = "admin")]
    [HttpGet("admin/recent")]
    public async Task<IActionResult> GetAdminRecent([FromQuery] int limit = 50)
    {
        var resultado = await _walletLn.GetAdminRecentTransactionsAsync(limit);
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var sub = User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out userId);
    }
}
