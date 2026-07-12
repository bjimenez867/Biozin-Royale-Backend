namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TFinanzasSummaryResultado
{
    public int    DepositCount    { get; set; }
    public decimal DepositTotal   { get; set; }
    public int    WithdrawalCount { get; set; }
    public decimal WithdrawalTotal { get; set; }
    public int    BetCount        { get; set; }
    public decimal BetTotal       { get; set; }
}
