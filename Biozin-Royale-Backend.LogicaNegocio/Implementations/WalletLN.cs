using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class WalletLN : IWalletLN
{
    private readonly IUnitWork _unitOfWork;

    public WalletLN(IUnitWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<Response<decimal>> GetBalanceAsync(Guid userId)
    {
        var resultado = new Response<decimal>();

        var walletResult = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId);
        if (walletResult.ReturnValue is null)
        {
            resultado.lpError("Wallet", "Billetera no encontrada.");
            return Task.FromResult(resultado);
        }

        resultado.ReturnValue = walletResult.ReturnValue.Balance;
        return Task.FromResult(resultado);
    }

    public Task<Response<decimal>> UpdateBalanceAsync(Guid userId, decimal newBalance)
    {
        var resultado = new Response<decimal>();

        if (newBalance < 0)
        {
            resultado.lpError("Saldo inválido", "El saldo no puede ser negativo.");
            return Task.FromResult(resultado);
        }

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId).ReturnValue;
        if (wallet is null)
        {
            resultado.lpError("Wallet", "Billetera no encontrada.");
            return Task.FromResult(resultado);
        }

        wallet.Balance = Math.Round(newBalance, 2);
        wallet.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Wallets.Modificar(wallet);
        _unitOfWork.Completar();

        resultado.ReturnValue = wallet.Balance;
        return Task.FromResult(resultado);
    }

    public Task<Response<IEnumerable<TWalletTransaccionResultado>>> GetTransactionsAsync(Guid userId)
    {
        var resultado = new Response<IEnumerable<TWalletTransaccionResultado>>();

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId).ReturnValue;
        if (wallet is null)
        {
            resultado.lpError("Wallet", "Billetera no encontrada.");
            return Task.FromResult(resultado);
        }

        var movimientos = _unitOfWork.WalletTransactions
            .ObtenerEntidades(t => t.WalletId == wallet.Id
                && (t.TransactionType == "deposit" || t.TransactionType == "withdrawal"))
            .ReturnValue!
            .OrderByDescending(t => t.CreatedAt);

        resultado.ReturnValue = movimientos.Select(t => new TWalletTransaccionResultado
        {
            Id = t.Id,
            TransactionType = t.TransactionType,
            Status = t.Status,
            Amount = t.Amount,
            BalanceBefore = t.BalanceBefore,
            BalanceAfter = t.BalanceAfter,
            ReferenceType = t.ReferenceType,
            CreatedAt = t.CreatedAt
        });

        return Task.FromResult(resultado);
    }
}
