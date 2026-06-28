using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
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
}
