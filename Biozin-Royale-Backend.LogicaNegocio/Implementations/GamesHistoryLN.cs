using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class GamesHistoryLN : IGamesHistoryLN
{
    private readonly IUnitWork _unitOfWork;

    public GamesHistoryLN(IUnitWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<Response<IEnumerable<TGamesHistory>>> ObtenerHistorialAsync(Guid userId)
    {
        var resultado = new Response<IEnumerable<TGamesHistory>>();

        // Igual que user_statistics: solo apuestas ya resueltas, nunca las pendientes.
        var apuestas = _unitOfWork.GamesHistory
            .ObtenerEntidades(b => b.UserId == userId && b.Status == "settled")
            .ReturnValue!
            .OrderByDescending(b => b.CreatedAt);

        resultado.ReturnValue = apuestas.Select(b => new TGamesHistory
        {
            Id = b.Id,
            GameType = b.GameType,
            Amount = b.Amount,
            Payout = b.Payout,
            Profit = b.Profit,
            Result = b.Result,
            CreatedAt = b.CreatedAt
        });

        return Task.FromResult(resultado);
    }
}
