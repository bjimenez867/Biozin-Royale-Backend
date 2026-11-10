namespace Biozin_Royale_Backend.Dominio.Entities;

public class StorePayment
{
    public Guid      Id                        { get; set; }
    public Guid      OrderId                   { get; set; }
    public string    Method                    { get; set; } = string.Empty;   // wallet | card | paypal
    public string    Status                    { get; set; } = "pending";      // pending | confirmed | rejected | refunded
    public decimal   Amount                    { get; set; }
    public Guid?     WalletTransactionId       { get; set; }
    public Guid?     RefundWalletTransactionId { get; set; }
    public Guid?     PaymentMethodId           { get; set; }
    public string?   CardLastFour              { get; set; }
    public string?   CardNetwork               { get; set; }
    public string?   Provider                  { get; set; }
    public string?   ProviderRef               { get; set; }
    public Guid?     VerifiedById              { get; set; }
    public string?   VerifiedByName            { get; set; }
    public DateTime? VerifiedAt                { get; set; }
    public string?   RejectedReason            { get; set; }
    public DateTime? RefundedAt                { get; set; }
    public DateTime  CreatedAt                 { get; set; }
    public DateTime  UpdatedAt                 { get; set; }
}
