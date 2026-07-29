namespace Biozin_Royale_Backend.Dominio.Entities;

public class PaymentMethod
{
    public Guid     Id            { get; set; }
    public Guid     WalletId      { get; set; }
    public string   Type          { get; set; } = string.Empty;  // "paypal" | "card"
    public string?  Alias         { get; set; }
    public bool     IsDefault     { get; set; }
    public DateTime CreatedAt     { get; set; }
    public DateTime UpdatedAt     { get; set; }

    // PayPal
    public string?  Email         { get; set; }

    // Tarjeta
    public string?  CardHolder    { get; set; }
    public string?  LastFour      { get; set; }
    public string?  Network       { get; set; }  // "visa" | "mastercard" | "amex" | "other"
    public int?     ExpiryMonth   { get; set; }
    public int?     ExpiryYear    { get; set; }
}
