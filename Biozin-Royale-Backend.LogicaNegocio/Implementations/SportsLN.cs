using System.Text.Json;
using System.Text.Json.Serialization;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;
using Microsoft.Extensions.Configuration;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class SportsLN : ISportsLN
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(30);

    private IEnumerable<TSportsMatch>? _cache;
    private DateTime _cacheTs = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly (string Key, string Sport, string League)[] SportConfig =
    [
        ("soccer_fifa_world_cup",             "football", "Mundial 2026"),
        ("soccer_epl",                        "football", "Premier League"),
        ("soccer_italy_serie_a",              "football", "Serie A"),
        ("soccer_conmebol_copa_libertadores", "football", "Copa Libertadores"),
        ("soccer_conmebol_copa_sudamericana", "football", "Copa Sudamericana"),
    ];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public SportsLN(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["OddsApi:Key"]!;
    }

    public async Task<Response<IEnumerable<TSportsMatch>>> GetMatchesAsync()
    {
        var resultado = new Response<IEnumerable<TSportsMatch>>();

        if (_cache != null && DateTime.UtcNow - _cacheTs < _ttl)
        {
            resultado.ReturnValue = _cache;
            return resultado;
        }

        await _lock.WaitAsync();
        try
        {
            // Double-check inside lock
            if (_cache != null && DateTime.UtcNow - _cacheTs < _ttl)
            {
                resultado.ReturnValue = _cache;
                return resultado;
            }

            var client = _httpClientFactory.CreateClient("OddsApi");
            var tasks = SportConfig.Select(async cfg =>
            {
                try
                {
                    var url = $"sports/{cfg.Key}/odds/?apiKey={_apiKey}&regions=eu&markets=h2h&oddsFormat=decimal";
                    var json = await client.GetStringAsync(url);
                    var events = JsonSerializer.Deserialize<List<ApiEvent>>(json, JsonOpts) ?? [];
                    return (events, cfg);
                }
                catch
                {
                    return (new List<ApiEvent>(), cfg);
                }
            });

            var results = await Task.WhenAll(tasks);
            var matches = new List<TSportsMatch>();

            foreach (var (events, cfg) in results)
            {
                foreach (var ev in events.Take(4))
                {
                    var match = MapEvent(ev, cfg.Sport, cfg.League);
                    if (match is not null) matches.Add(match);
                }
            }

            _cache = matches;
            _cacheTs = DateTime.UtcNow;
            resultado.ReturnValue = matches;
        }
        catch (Exception ex)
        {
            resultado.lpError("Deportes", $"Error al obtener partidos: {ex.Message}");
        }
        finally
        {
            _lock.Release();
        }

        return resultado;
    }

    private static TSportsMatch? MapEvent(ApiEvent ev, string sport, string league)
    {
        var bookmaker = ev.Bookmakers.FirstOrDefault(b => b.Markets.Any(m => m.Key == "h2h"));
        if (bookmaker is null) return null;

        var market = bookmaker.Markets.FirstOrDefault(m => m.Key == "h2h");
        if (market is null) return null;

        var home = market.Outcomes.FirstOrDefault(o => o.Name == ev.HomeTeam);
        var away = market.Outcomes.FirstOrDefault(o => o.Name == ev.AwayTeam);
        var draw = market.Outcomes.FirstOrDefault(o => o.Name == "Draw");

        if (home is null || away is null) return null;

        return new TSportsMatch
        {
            Id = StableId(ev.Id),
            League = league,
            Sport = sport,
            Time = FormatTime(ev.CommenceTime),
            Team1 = ev.HomeTeam,
            Team2 = ev.AwayTeam,
            Odds = new TSportsOdds
            {
                Home = Math.Round(home.Price, 2),
                Draw = draw is not null ? Math.Round(draw.Price, 2) : null,
                Away = Math.Round(away.Price, 2),
            },
        };
    }

    private static string FormatTime(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var today = DateTime.Today;
        var eventDay = local.Date;
        var hhmm = local.ToString("HH:mm");

        if (eventDay == today)              return $"HOY {hhmm}";
        if (eventDay == today.AddDays(1))   return $"MÑN {hhmm}";
        return $"{local:dd/MM} {hhmm}";
    }

    private static int StableId(string s)
    {
        int h = 5381;
        foreach (var c in s) h = ((h << 5) + h) ^ c;
        return Math.Abs(h) % 900_000 + 100_000;
    }

    // ── API response models ───────────────────────────────────
    private record ApiEvent(
        [property: JsonPropertyName("id")]           string Id,
        [property: JsonPropertyName("commence_time")] DateTime CommenceTime,
        [property: JsonPropertyName("home_team")]    string HomeTeam,
        [property: JsonPropertyName("away_team")]    string AwayTeam,
        [property: JsonPropertyName("bookmakers")]   List<ApiBookmaker> Bookmakers
    );

    private record ApiBookmaker(
        [property: JsonPropertyName("key")]     string Key,
        [property: JsonPropertyName("markets")] List<ApiMarket> Markets
    );

    private record ApiMarket(
        [property: JsonPropertyName("key")]      string Key,
        [property: JsonPropertyName("outcomes")] List<ApiOutcome> Outcomes
    );

    private record ApiOutcome(
        [property: JsonPropertyName("name")]  string Name,
        [property: JsonPropertyName("price")] decimal Price
    );
}
