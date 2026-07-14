namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TReportesKpiResultado
{
    public int     ActiveUsers     { get; set; }
    public decimal DepositTotal    { get; set; }
    public decimal WithdrawalTotal { get; set; }
    public decimal NetProfit       { get; set; }
}
