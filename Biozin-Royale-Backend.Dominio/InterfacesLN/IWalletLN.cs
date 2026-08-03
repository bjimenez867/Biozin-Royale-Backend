using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IWalletLN
{
    Task<Response<decimal>> GetBalanceAsync(Guid userId);
    Task<Response<IEnumerable<TWalletTransaccionResultado>>> GetTransactionsAsync(Guid userId);
    Task<Response<TFinanzasSummaryResultado>> GetAdminSummaryAsync();
    Task<Response<IEnumerable<TFinanzasTransaccionResultado>>> GetAdminRecentTransactionsAsync(int limit = 50);
}
