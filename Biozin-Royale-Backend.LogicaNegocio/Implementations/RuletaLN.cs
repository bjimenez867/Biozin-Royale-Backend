using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using System.Security.Cryptography;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class RuletaLN : IRuletaLN
{
    private readonly IUnitWork _unitOfWork;

    private static readonly HashSet<int> Reds =
        [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36];

    private static readonly HashSet<string> ValidBetIds = BuildValidBetIds();

    public RuletaLN(IUnitWork unitOfWork) => _unitOfWork = unitOfWork;

    public Task<Response<TRuletaSpinResult>> SpinAsync(Guid userId, Dictionary<string, decimal> bets)
    {
        var resultado = new Response<TRuletaSpinResult>();

        if (bets is null || bets.Count == 0)
        {
            resultado.lpError("Sin apuesta", "Debes colocar al menos una apuesta.");
            return Task.FromResult(resultado);
        }

        foreach (var (id, stake) in bets)
        {
            if (!ValidBetIds.Contains(id))
            {
                resultado.lpError("Apuesta inválida", $"ID de apuesta no reconocido: {id}");
                return Task.FromResult(resultado);
            }
            if (stake <= 0)
            {
                resultado.lpError("Apuesta inválida", "Cada apuesta debe ser mayor a cero.");
                return Task.FromResult(resultado);
            }
        }

        var totalStake = bets.Values.Sum();

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId).ReturnValue;
        if (wallet is null)
        {
            resultado.lpError("Error", "Billetera no encontrada.");
            return Task.FromResult(resultado);
        }

        if (wallet.Balance < totalStake)
        {
            resultado.lpError("Fondos insuficientes", "No tienes saldo suficiente para esta apuesta.");
            return Task.FromResult(resultado);
        }

        var winning = RandomNumberGenerator.GetInt32(37); // 0–36 inclusive

        decimal win = 0;
        foreach (var (id, stake) in bets)
        {
            if (BetWins(id, winning))
                win += stake * (GetPayout(id) + 1);
        }
        win = Math.Round(win, 2);

        wallet.Balance = Math.Round(wallet.Balance - totalStake + win, 2);
        wallet.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Wallets.Modificar(wallet);

        _unitOfWork.GamesHistory.Insertar(new GamesHistory
        {
            Id        = Guid.NewGuid(),
            UserId    = userId,
            GameType  = "roulette",
            Amount    = totalStake,
            Payout    = win,
            Profit    = Math.Round(win - totalStake, 2),
            Result    = win > 0 ? "win" : "loss",
            Status    = "settled",
            SettledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        _unitOfWork.Completar();

        resultado.ReturnValue = new TRuletaSpinResult
        {
            WinningNumber = winning,
            Win           = win,
            NewBalance    = wallet.Balance,
        };

        return Task.FromResult(resultado);
    }

    private static bool BetWins(string id, int w) => id switch
    {
        "red"   => Reds.Contains(w),
        "black" => w != 0 && !Reds.Contains(w),
        "even"  => w != 0 && w % 2 == 0,
        "odd"   => w % 2 == 1,
        "low"   => w >= 1 && w <= 18,
        "high"  => w >= 19 && w <= 36,
        "d1"    => w >= 1  && w <= 12,
        "d2"    => w >= 13 && w <= 24,
        "d3"    => w >= 25 && w <= 36,
        "c1"    => w != 0 && w % 3 == 1,
        "c2"    => w != 0 && w % 3 == 2,
        "c3"    => w != 0 && w % 3 == 0,
        _       => int.TryParse(id, out var n) && n == w,
    };

    private static int GetPayout(string id) => id switch
    {
        "d1" or "d2" or "d3"
        or "c1" or "c2" or "c3" => 2,
        "red" or "black" or "even" or "odd"
        or "low" or "high"       => 1,
        _                        => 35,
    };

    private static HashSet<string> BuildValidBetIds()
    {
        var ids = new HashSet<string> { "red","black","even","odd","low","high","d1","d2","d3","c1","c2","c3" };
        for (int i = 0; i <= 36; i++) ids.Add(i.ToString());
        return ids;
    }
}
