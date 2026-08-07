namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TSlotsSpinRequest
{
    public decimal Bet { get; set; }
}

public class TSlotsSpinResult
{
    public string[][] Grid { get; set; } = [];
    public decimal Win { get; set; }
    public decimal NewBalance { get; set; }
    public bool BigWin { get; set; }
    public List<TSlotsHit> Hits { get; set; } = [];
}

public class TSlotsHit
{
    public int LineIndex { get; set; }
    public int Length { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
