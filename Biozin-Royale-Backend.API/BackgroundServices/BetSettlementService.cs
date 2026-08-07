using System.Text.Json;
using System.Text.Json.Serialization;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.TypedEntities;

namespace Biozin_Royale_Backend.API.BackgroundServices;

/// Liquida apuestas deportivas pendientes contra los resultados de the-odds-api.
/// Corre cada 10 min; solo llama a la API si hay selecciones pendientes cuyo
/// partido ya inició.
public class BetSettlementService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BetSettlementService> _logger;

    public BetSettlementService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<BetSettlementService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SettleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error liquidando apuestas deportivas.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SettleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitWork>();

        var pendingBets = unitOfWork.GamesHistory
            .ObtenerEntidades(b => b.GameType == "sports" && b.Status == "pending")
            .ReturnValue!
            .ToList();

        if (pendingBets.Count == 0) return;

        var now = DateTime.UtcNow;
        var betData = pendingBets
            .Select(b => (
                Bet: b,
                Selections: string.IsNullOrEmpty(b.Selections)
                    ? new List<TBetSelectionDetail>()
                    : JsonSerializer.Deserialize<List<TBetSelectionDetail>>(b.Selections) ?? []))
            .ToList();

        var duePairs = betData
            .SelectMany(x => x.Selections.Select(s => (x.Bet, Selection: s)))
            .Where(p => p.Selection.Status == "pending" && p.Selection.CommenceTimeUtc <= now)
            .ToList();

        if (duePairs.Count == 0) return;

        var apiKey = _configuration["OddsApi:Key"];
        var client = _httpClientFactory.CreateClient("OddsApi");
        var scoreEvents = new Dictionary<string, ScoreEvent>();

        foreach (var group in duePairs.GroupBy(p => p.Selection.SportKey))
        {
            var ids = string.Join(",", group.Select(p => p.Selection.ExternalMatchId).Distinct());
            try
            {
                var url = $"sports/{group.Key}/scores/?apiKey={apiKey}&daysFrom=3&eventIds={ids}";
                var json = await client.GetStringAsync(url, ct);
                var events = JsonSerializer.Deserialize<List<ScoreEvent>>(json, JsonOpts) ?? [];
                foreach (var ev in events) scoreEvents[ev.Id] = ev;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudieron obtener resultados para {SportKey}", group.Key);
            }
        }

        if (scoreEvents.Count == 0) return;

        var touchedBetIds = new HashSet<Guid>();
        foreach (var (bet, selection) in duePairs)
        {
            if (!scoreEvents.TryGetValue(selection.ExternalMatchId, out var ev) || !ev.Completed || ev.Scores is null)
                continue;

            var homeScore = ParseScore(ev.Scores, ev.HomeTeam);
            var awayScore = ParseScore(ev.Scores, ev.AwayTeam);
            if (homeScore is null || awayScore is null) continue;

            var winner = homeScore == awayScore ? "draw" : homeScore > awayScore ? "home" : "away";
            selection.Status = selection.Outcome == winner ? "won" : "lost";
            touchedBetIds.Add(bet.Id);
        }

        if (touchedBetIds.Count == 0) return;

        foreach (var betId in touchedBetIds)
        {
            var (bet, selections) = betData.First(x => x.Bet.Id == betId);
            bet.Selections = JsonSerializer.Serialize(selections);

            if (selections.Any(s => s.Status == "pending"))
            {
                unitOfWork.GamesHistory.Modificar(bet);
                continue;
            }

            var won = selections.All(s => s.Status == "won");
            bet.Result    = won ? "won" : "lost";
            bet.Status    = "settled";
            bet.SettledAt = now;
            bet.Payout    = won
                ? Math.Round(bet.Amount * selections.Aggregate(1m, (acc, s) => acc * s.Odds), 2)
                : 0m;
            unitOfWork.GamesHistory.Modificar(bet);

            if (won)
            {
                var wallet = unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == bet.UserId).ReturnValue;
                if (wallet is not null)
                {
                    var before = wallet.Balance;
                    wallet.Balance   = Math.Round(wallet.Balance + bet.Payout, 2);
                    wallet.UpdatedAt = now;
                    unitOfWork.Wallets.Modificar(wallet);

                    unitOfWork.WalletTransactions.Insertar(new WalletTransaction
                    {
                        Id              = Guid.NewGuid(),
                        WalletId        = wallet.Id,
                        TransactionType = "bet_win",
                        Status          = "completed",
                        Amount          = bet.Payout,
                        BalanceBefore   = before,
                        BalanceAfter    = wallet.Balance,
                        ReferenceType   = "bet",
                        ReferenceId     = bet.Id,
                        CreatedAt       = now,
                    });
                }
            }
        }

        unitOfWork.Completar();
    }

    private static decimal? ParseScore(List<ScoreEntry> scores, string teamName)
    {
        var entry = scores.FirstOrDefault(s => s.Name == teamName);
        return entry?.Score is not null && decimal.TryParse(entry.Score, out var val) ? val : null;
    }

    private record ScoreEvent(
        [property: JsonPropertyName("id")]        string Id,
        [property: JsonPropertyName("completed")] bool Completed,
        [property: JsonPropertyName("home_team")] string HomeTeam,
        [property: JsonPropertyName("away_team")] string AwayTeam,
        [property: JsonPropertyName("scores")]    List<ScoreEntry>? Scores
    );

    private record ScoreEntry(
        [property: JsonPropertyName("name")]  string Name,
        [property: JsonPropertyName("score")] string? Score
    );
}
