namespace Biozin_Royale_Backend.Dominio.Entities;

public class StoreOrder
{
    public Guid      Id               { get; set; }
    public int       OrderNumber      { get; set; }
    public Guid      UserId           { get; set; }
    public string    CustomerName     { get; set; } = string.Empty;
    public string    Phone            { get; set; } = string.Empty;
    public string    Province         { get; set; } = string.Empty;
    public string?   Canton           { get; set; }
    public string?   District         { get; set; }
    public string    AddressLine      { get; set; } = string.Empty;
    public decimal   Subtotal         { get; set; }
    public decimal   ShippingCost     { get; set; }
    public decimal   Total            { get; set; }
    public string    PaymentStatus    { get; set; } = "pending";   // pending | confirmed | rejected | refunded
    public short     FulfillmentPhase { get; set; }                // 0..6
    public bool      IsCancelled      { get; set; }
    public DateTime? CancelledAt      { get; set; }
    public Guid?     CancelledById    { get; set; }
    public string?   CancelledByRole  { get; set; }
    public string?   CancelReason     { get; set; }
    public string?   CarrierName      { get; set; }
    public string?   TrackingNumber   { get; set; }
    public string?   PackageInfo      { get; set; }
    public string    DeliveryCode     { get; set; } = string.Empty;
    public short     CodeAttempts     { get; set; }
    public DateTime? CodeLockedUntil  { get; set; }
    public bool      HasOpenIncident  { get; set; }
    public string?   RescheduleWindow { get; set; }
    public DateTime? EtaFrom          { get; set; }
    public DateTime? EtaTo            { get; set; }
    public short?    Rating           { get; set; }
    public DateTime? RatedAt          { get; set; }
    public string?   IdempotencyKey   { get; set; }
    public DateTime  CreatedAt        { get; set; }
    public DateTime  UpdatedAt        { get; set; }
}
