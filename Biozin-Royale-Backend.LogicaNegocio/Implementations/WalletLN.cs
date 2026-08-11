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

    public async Task<Response<decimal>> GetBalanceAsync(Guid userId)
    {
        var resultado = new Response<decimal>();

        var wallet = await _unitOfWork.Wallets.ObtenerEntidadAsync(w => w.UserId == userId);
        if (wallet is null)
        {
            resultado.lpError("Wallet", "Billetera no encontrada.");
            return resultado;
        }

        resultado.ReturnValue = wallet.Balance;
        return resultado;
    }

    public async Task<Response<IEnumerable<TWalletTransaccionResultado>>> GetTransactionsAsync(Guid userId)
    {
        var resultado = new Response<IEnumerable<TWalletTransaccionResultado>>();

        var wallet = await _unitOfWork.Wallets.ObtenerEntidadAsync(w => w.UserId == userId);
        if (wallet is null)
        {
            resultado.lpError("Wallet", "Billetera no encontrada.");
            return resultado;
        }

        var movimientos = await _unitOfWork.WalletTransactions
            .ObtenerEntidadesAsync(t => t.WalletId == wallet.Id
                && (t.TransactionType == "deposit" || t.TransactionType == "withdrawal"));

        resultado.ReturnValue = movimientos
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TWalletTransaccionResultado
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

        return resultado;
    }

    public async Task<Response<TFinanzasSummaryResultado>> GetAdminSummaryAsync()
    {
        var resultado = new Response<TFinanzasSummaryResultado>();
        var hoy = DateTime.UtcNow.Date;
        var manana = hoy.AddDays(1);

        var depositos = await _unitOfWork.WalletTransactions
            .ObtenerEntidadesAsync(t => t.TransactionType == "deposit"
                && t.Status == "completed"
                && t.CreatedAt >= hoy && t.CreatedAt < manana);

        var retiros = await _unitOfWork.WalletTransactions
            .ObtenerEntidadesAsync(t => t.TransactionType == "withdrawal"
                && t.Status == "completed"
                && t.CreatedAt >= hoy && t.CreatedAt < manana);

        var apuestas = await _unitOfWork.GamesHistory
            .ObtenerEntidadesAsync(b => b.CreatedAt >= hoy && b.CreatedAt < manana);

        resultado.ReturnValue = new TFinanzasSummaryResultado
        {
            DepositCount    = depositos.Count,
            DepositTotal    = depositos.Sum(t => t.Amount),
            WithdrawalCount = retiros.Count,
            WithdrawalTotal = retiros.Sum(t => t.Amount),
            BetCount        = apuestas.Count,
            BetTotal        = apuestas.Sum(b => b.Amount),
        };

        return resultado;
    }

    public async Task<Response<IEnumerable<TFinanzasTransaccionResultado>>> GetAdminRecentTransactionsAsync(int limit = 50)
    {
        var resultado = new Response<IEnumerable<TFinanzasTransaccionResultado>>();

        var recientes = (await _unitOfWork.WalletTransactions
            .ObtenerEntidadesAsync(t => t.TransactionType == "deposit" || t.TransactionType == "withdrawal"))
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .ToList();

        var walletIds = recientes.Select(t => t.WalletId).Distinct().ToList();
        var walletMap = (await _unitOfWork.Wallets
            .ObtenerEntidadesAsync(w => walletIds.Contains(w.Id)))
            .ToDictionary(w => w.Id, w => w.UserId);

        var userIds = walletMap.Values.Distinct().ToList();
        var profileMap = (await _unitOfWork.Profiles
            .ObtenerEntidadesAsync(p => userIds.Contains(p.UserId)))
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

        return resultado;
    }
}
