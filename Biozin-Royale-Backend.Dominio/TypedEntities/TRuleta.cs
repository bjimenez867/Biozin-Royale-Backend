namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TRuletaSpinRequest
{
    public Dictionary<string, decimal> Bets { get; set; } = [];
}

public class TRuletaSpinResult
{
    public int     WinningNumber { get; set; }
    public decimal Win           { get; set; }
    public decimal NewBalance    { get; set; }
}
