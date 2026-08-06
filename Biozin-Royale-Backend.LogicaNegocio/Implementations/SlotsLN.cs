using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;
using System.Security.Cryptography;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class SlotsLN : ISlotsLN
{
    private readonly IUnitWork _unitOfWork;

    private static readonly decimal[] ValidBets = [5m, 10m, 20m, 50m, 100m, 200m];
    private const int Cols = 4;
    private const int Rows = 4;

    // Mismos pesos y multiplicadores que el frontend (slots.symbols.ts)
    private static readonly (string Name, decimal Mult, int Weight)[] SymbolDefs =
    [
        ("ring",   2.5m,  4),
        ("crown",  1.5m,  6),
        ("red",    0.8m, 11),
        ("purple", 0.6m, 13),
        ("blue",   0.5m, 14),
        ("green",  0.4m, 16),
        ("orange", 0.35m, 17),
        ("yellow", 0.3m, 19),
    ];

    private static readonly Dictionary<string, decimal> SymbolMult =
        SymbolDefs.ToDictionary(s => s.Name, s => s.Mult);

    private static readonly string[] WeightedPool = BuildPool();

    // Mismas 10 líneas que el frontend: LINES[lineIdx][colIdx] = rowIndex
    private static readonly int[][] Lines =
    [
        [0, 0, 0, 0],
        [1, 1, 1, 1],
        [2, 2, 2, 2],
        [3, 3, 3, 3],
        [0, 1, 2, 3],
        [3, 2, 1, 0],
        [0, 1, 1, 0],
        [3, 2, 2, 3],
        [1, 2, 2, 1],
        [2, 1, 1, 2],
    ];

    private static readonly Dictionary<int, decimal> LenFactor = new()
    {
        [3] = 4m,
        [4] = 28m,
    };

    public SlotsLN(IUnitWork unitOfWork) => _unitOfWork = unitOfWork;

    public Task<Response<TSlotsSpinResult>> SpinAsync(Guid userId, decimal bet)
    {
        var resultado = new Response<TSlotsSpinResult>();

        if (!ValidBets.Contains(bet))
        {
            resultado.lpError("Apuesta inválida", "El monto de apuesta no es válido.");
            return Task.FromResult(resultado);
        }

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId).ReturnValue;
        if (wallet is null)
        {
            resultado.lpError("Error", "Billetera no encontrada.");
            return Task.FromResult(resultado);
        }

        if (wallet.Balance < bet)
        {
            resultado.lpError("Fondos insuficientes", "No tienes saldo suficiente para jugar.");
            return Task.FromResult(resultado);
        }

        var grid = GenerateGrid();
        var (win, hits) = EvalLines(grid, bet);

        var balanceBefore = wallet.Balance;
        wallet.Balance = Math.Round(wallet.Balance - bet + win, 2);
        wallet.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Wallets.Modificar(wallet);

        var spinRecord = new GamesHistory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GameType = "slots",
            Amount = bet,
            Payout = win,
            Profit = Math.Round(win - bet, 2),
            Result = win > 0 ? "win" : "loss",
            Status = "settled",
            SettledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        _unitOfWork.GamesHistory.Insertar(spinRecord);
        _unitOfWork.Completar();

        resultado.ReturnValue = new TSlotsSpinResult
        {
            Grid = grid,
            Win = win,
            NewBalance = wallet.Balance,
            BigWin = hits.Any(h => h.Length >= 4) || win >= bet * 8,
            Hits = hits,
        };

        return Task.FromResult(resultado);
    }

    private static string[][] GenerateGrid()
    {
        var grid = new string[Cols][];
        for (int c = 0; c < Cols; c++)
        {
            grid[c] = new string[Rows];
            for (int r = 0; r < Rows; r++)
                grid[c][r] = WeightedPool[RandomNumberGenerator.GetInt32(WeightedPool.Length)];
        }
        return grid;
    }

    private static (decimal Win, List<TSlotsHit> Hits) EvalLines(string[][] grid, decimal bet)
    {
        decimal win = 0;
        var hits = new List<TSlotsHit>();

        for (int li = 0; li < Lines.Length; li++)
        {
            var line = Lines[li];
            var first = grid[0][line[0]];
            var len = 1;

            for (int c = 1; c < Cols; c++)
            {
                if (grid[c][line[c]] == first) len++;
                else break;
            }

            if (len >= 3 && LenFactor.TryGetValue(len, out var factor))
            {
                var amt = Math.Round(bet * SymbolMult[first] * factor, 2);
                win += amt;
                hits.Add(new TSlotsHit
                {
                    LineIndex = li,
                    Length = len,
                    Symbol = first,
                    Amount = amt,
                });
            }
        }

        return (Math.Round(win, 2), hits);
    }

    private static string[] BuildPool()
    {
        var pool = new List<string>();
        foreach (var (name, _, weight) in SymbolDefs)
            for (int i = 0; i < weight; i++)
                pool.Add(name);
        return [.. pool];
    }
}
