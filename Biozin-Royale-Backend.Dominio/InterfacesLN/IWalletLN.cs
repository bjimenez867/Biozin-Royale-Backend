using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IWalletLN
{
    Task<Response<decimal>> GetBalanceAsync(Guid userId);
}
