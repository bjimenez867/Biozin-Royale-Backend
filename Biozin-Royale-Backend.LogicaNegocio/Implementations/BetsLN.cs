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

        var profileResult = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId);
        var profile = profileResult.ReturnValue;

        if (profile is null)
        {
            resultado.lpError("Error", "Perfil no encontrado.");
            return Task.FromResult(resultado);
        }

        // El frontend ya bloquea esto con un guard, pero el endpoint no debe confiar
        // solo en eso: un invitado no debe poder apostar dinero real aunque le pegue
        // directo a la API.
        if (profile.IsGuest)
        {
            resultado.lpError("Cuenta de invitado", "Crea una cuenta para poder apostar.");
            return Task.FromResult(resultado);
        }

        if (profile.Balance < request.Amount)
        {
            resultado.lpError("Fondos insuficientes", "No tienes saldo suficiente para realizar esta apuesta.");
            return Task.FromResult(resultado);
        }

        var potentialWin = Math.Round(request.Amount * request.TotalOdds, 2);
        profile.Balance = Math.Round(profile.Balance - request.Amount, 2);
        _unitOfWork.Profiles.Modificar(profile);

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
            NewBalance = profile.Balance,
            PotentialWin = potentialWin,
        };

        return Task.FromResult(resultado);
    }
}
