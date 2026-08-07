namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TIniciarRetiroRequest
{
    public Guid    PaymentMethodId { get; set; }
    public decimal Amount          { get; set; }
}

public class TRetiroResultado
{
    public Guid    TransactionId { get; set; }
    public decimal NewBalance    { get; set; }
    public string? ReceiptNumber { get; set; }
}
