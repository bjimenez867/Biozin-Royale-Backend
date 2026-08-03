namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TMatchLookup
{
    public string ExternalId { get; set; } = string.Empty;
    public string SportKey { get; set; } = string.Empty;
    public DateTime CommenceTimeUtc { get; set; }
    public string Sport { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public string Team1 { get; set; } = string.Empty;
    public string Team2 { get; set; } = string.Empty;
    public TSportsOdds Odds { get; set; } = new();
}
