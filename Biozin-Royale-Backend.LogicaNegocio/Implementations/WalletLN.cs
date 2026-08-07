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
            Id              = t.Id,
            TransactionType = t.TransactionType,
            Status          = t.Status,
            Amount          = t.Amount,
            BalanceBefore   = t.BalanceBefore,
            BalanceAfter    = t.BalanceAfter,
            ReferenceType   = t.ReferenceType,
            ReceiptNumber   = t.ReceiptNumber,
            CreatedAt       = t.CreatedAt,
        });

        return Task.FromResult(resultado);
    }

    public Task<Response<TFinanzasSummaryResultado>> GetAdminSummaryAsync()
    {
        var resultado = new Response<TFinanzasSummaryResultado>();
        var hoy = DateTime.UtcNow.Date;
        var manana = hoy.AddDays(1);

        var depositos = _unitOfWork.WalletTransactions
            .ObtenerEntidades(t => t.TransactionType == "deposit"
                && t.Status == "completed"
                && t.CreatedAt >= hoy && t.CreatedAt < manana)
            .ReturnValue!.ToList();

        var retiros = _unitOfWork.WalletTransactions
            .ObtenerEntidades(t => t.TransactionType == "withdrawal"
                && t.Status == "completed"
                && t.CreatedAt >= hoy && t.CreatedAt < manana)
            .ReturnValue!.ToList();

        var apuestas = _unitOfWork.GamesHistory
            .ObtenerEntidades(b => b.CreatedAt >= hoy && b.CreatedAt < manana)
            .ReturnValue!.ToList();

        resultado.ReturnValue = new TFinanzasSummaryResultado
        {
            DepositCount    = depositos.Count,
            DepositTotal    = depositos.Sum(t => t.Amount),
            WithdrawalCount = retiros.Count,
            WithdrawalTotal = retiros.Sum(t => t.Amount),
            BetCount        = apuestas.Count,
            BetTotal        = apuestas.Sum(b => b.Amount),
        };

        return Task.FromResult(resultado);
    }

    public Task<Response<IEnumerable<TFinanzasTransaccionResultado>>> GetAdminRecentTransactionsAsync(int limit = 50)
    {
        var resultado = new Response<IEnumerable<TFinanzasTransaccionResultado>>();

        var recientes = _unitOfWork.WalletTransactions
            .ObtenerEntidades(t => t.TransactionType == "deposit" || t.TransactionType == "withdrawal")
            .ReturnValue!
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .ToList();

        var walletIds = recientes.Select(t => t.WalletId).Distinct().ToList();
        var walletMap = _unitOfWork.Wallets
            .ObtenerEntidades(w => walletIds.Contains(w.Id))
            .ReturnValue!
            .ToDictionary(w => w.Id, w => w.UserId);

        var userIds = walletMap.Values.Distinct().ToList();
        var profileMap = _unitOfWork.Profiles
            .ObtenerEntidades(p => userIds.Contains(p.UserId))
            .ReturnValue!
            .ToDictionary(p => p.UserId, p => p);

        resultado.ReturnValue = recientes.Select(t =>
        {
            walletMap.TryGetValue(t.WalletId, out var uid);
            profileMap.TryGetValue(uid, out var perfil);
            return new TFinanzasTransaccionResultado
            {
                Id              = t.Id,
                TransactionType = t.TransactionType,
                Status          = t.Status,
                Amount          = t.Amount,
                CreatedAt       = t.CreatedAt,
                Username        = perfil?.Username    ?? "–",
                DisplayName     = perfil?.DisplayName ?? "–",
                ReceiptNumber   = t.ReceiptNumber,
            };
        });

        return Task.FromResult(resultado);
    }
}
