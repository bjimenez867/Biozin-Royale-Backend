using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class BetsLN : IBetsLN
{
    private readonly IUnitWork _unitOfWork;

    public BetsLN(IUnitWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<Response<TBetResult>> PlaceBetAsync(Guid userId, TPlaceBetRequest request)
    {
        var resultado = new Response<TBetResult>();

        if (request.Amount <= 0 || request.Selections.Count == 0)
        {
            resultado.lpError("Apuesta inválida", "El monto y las selecciones son requeridos.");
            return Task.FromResult(resultado);
        }

        var walletResult = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId);
        var wallet = walletResult.ReturnValue;

        if (wallet is null)
        {
            resultado.lpError("Error", "Billetera no encontrada.");
            return Task.FromResult(resultado);
        }

        if (wallet.Balance < request.Amount)
        {
            resultado.lpError("Fondos insuficientes", "No tienes saldo suficiente para realizar esta apuesta.");
            return Task.FromResult(resultado);
        }

        var potentialWin = Math.Round(request.Amount * request.TotalOdds, 2);
        wallet.Balance = Math.Round(wallet.Balance - request.Amount, 2);
        wallet.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Wallets.Modificar(wallet);

        var bet = new GamesHistory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GameType = "sports",
            Amount = request.Amount,
            Payout = 0m,
            Result = "pending",
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
        };
        _unitOfWork.GamesHistory.Insertar(bet);
        _unitOfWork.Completar();

        resultado.ReturnValue = new TBetResult
        {
            BetId = bet.Id,
            NewBalance = wallet.Balance,
            PotentialWin = potentialWin,
        };

        return Task.FromResult(resultado);
    }
}
