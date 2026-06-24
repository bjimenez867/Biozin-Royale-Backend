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

        var profileResult = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId);
        if (profileResult.ReturnValue is null)
        {
            resultado.lpError("Wallet", "Perfil no encontrado.");
            return Task.FromResult(resultado);
        }

        resultado.ReturnValue = profileResult.ReturnValue.Balance;
        return Task.FromResult(resultado);
    }
}
