using System.Text.Json;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class BetsLN : IBetsLN
{
    private readonly IUnitWork _unitOfWork;
    private readonly ISportsLN _sportsLn;

    public BetsLN(IUnitWork unitOfWork, ISportsLN sportsLn)
    {
        _unitOfWork = unitOfWork;
        _sportsLn = sportsLn;
    }

    public async Task<Response<TBetResult>> PlaceBetAsync(Guid userId, TPlaceBetRequest request)
    {
        var resultado = new Response<TBetResult>();

        if (request.Amount <= 0 || request.Selections.Count == 0)
        {
            resultado.lpError("Apuesta inválida", "El monto y las selecciones son requeridos.");
            return resultado;
        }

        var walletResult = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId);
        var wallet = walletResult.ReturnValue;

        if (wallet is null)
        {
            resultado.lpError("Error", "Billetera no encontrada.");
            return resultado;
        }

        if (wallet.Balance < request.Amount)
        {
            resultado.lpError("Fondos insuficientes", "No tienes saldo suficiente para realizar esta apuesta.");
            return resultado;
        }

        var selections = new List<TBetSelectionDetail>();
        foreach (var s in request.Selections)
        {
            var match = await _sportsLn.ResolveMatchAsync(s.MatchId);
            if (match is null || match.CommenceTimeUtc <= DateTime.UtcNow)
            {
                resultado.lpError("Apuesta inválida", "Uno de los partidos ya no está disponible para apostar.");
                return resultado;
            }

            var serverOdds = s.Outcome switch
            {
                "home" => (decimal?)match.Odds.Home,
                "away" => match.Odds.Away,
                "draw" => match.Odds.Draw,
                _      => null,
            };

            if (serverOdds is null)
            {
                resultado.lpError("Apuesta inválida", "La selección no es válida para este partido.");
                return resultado;
            }

            selections.Add(new TBetSelectionDetail
            {
                ExternalMatchId = match.ExternalId,
                SportKey        = match.SportKey,
                League          = s.League,
                Team1           = s.Team1,
                Team2           = s.Team2,
                Outcome         = s.Outcome,
                Odds            = serverOdds.Value,
                CommenceTimeUtc = match.CommenceTimeUtc,
                Status          = "pending",
            });
        }

        var serverTotalOdds = Math.Round(selections.Aggregate(1m, (acc, sel) => acc * sel.Odds), 2);
        var potentialWin    = Math.Round(request.Amount * serverTotalOdds, 2);
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
            Selections = JsonSerializer.Serialize(selections),
        };
        _unitOfWork.GamesHistory.Insertar(bet);
        _unitOfWork.Completar();

        resultado.ReturnValue = new TBetResult
        {
            BetId = bet.Id,
            NewBalance = wallet.Balance,
            PotentialWin = potentialWin,
        };

        return resultado;
    }

    public Task<Response<IEnumerable<TMyBet>>> GetMyBetsAsync(Guid userId)
    {
        var resultado = new Response<IEnumerable<TMyBet>>();
        var cutoff = DateTime.UtcNow.AddHours(-2);

        var bets = _unitOfWork.GamesHistory
            .ObtenerEntidades(b => b.UserId == userId
                && b.GameType == "sports"
                && (b.Status == "pending" || (b.Status == "settled" && b.SettledAt >= cutoff)))
            .ReturnValue!
            .OrderByDescending(b => b.CreatedAt);

        resultado.ReturnValue = bets.Select(b =>
        {
            var selections = string.IsNullOrEmpty(b.Selections)
                ? []
                : JsonSerializer.Deserialize<List<TBetSelectionDetail>>(b.Selections) ?? [];
            var totalOdds = selections.Count > 0
                ? selections.Aggregate(1m, (acc, s) => acc * s.Odds)
                : 1m;

            return new TMyBet
            {
                Id            = b.Id,
                Amount        = b.Amount,
                TotalOdds     = totalOdds,
                PotentialWin  = Math.Round(b.Amount * totalOdds, 2),
                Status        = b.Status == "settled" ? b.Result : "pending",
                Payout        = b.Payout,
                CreatedAt     = b.CreatedAt,
                SettledAt     = b.SettledAt,
                Selections    = selections,
            };
        });

        return Task.FromResult(resultado);
    }
}
