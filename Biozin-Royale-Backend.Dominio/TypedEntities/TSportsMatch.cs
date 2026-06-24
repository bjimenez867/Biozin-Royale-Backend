namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TSportsMatch
{
    public int Id { get; set; }
    public string League { get; set; } = string.Empty;
    public string Sport { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Team1 { get; set; } = string.Empty;
    public string Team2 { get; set; } = string.Empty;
    public TSportsOdds Odds { get; set; } = new();
}

public class TSportsOdds
{
    public decimal Home { get; set; }
    public decimal? Draw { get; set; }
    public decimal Away { get; set; }
}
