namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TIniciarDepositoResultado
{
    public Guid    TransactionId { get; set; }
    public string  Provider      { get; set; } = "";
    public string? ClientSecret  { get; set; }
    public string? OrderId       { get; set; }
    public decimal BalanceBefore { get; set; }
    public string? ReceiptNumber { get; set; }
}
