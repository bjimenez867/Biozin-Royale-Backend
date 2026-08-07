namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TFinanzasTransaccionResultado
{
    public Guid   Id              { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string Status          { get; set; } = string.Empty;
    public decimal Amount         { get; set; }
    public DateTime CreatedAt     { get; set; }
    public string Username        { get; set; } = string.Empty;
    public string DisplayName     { get; set; } = string.Empty;
    public string? ReceiptNumber  { get; set; }
}
