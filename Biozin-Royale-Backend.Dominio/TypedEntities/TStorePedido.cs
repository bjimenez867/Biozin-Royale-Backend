namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TStoreItemPedido
{
    public Guid    ProductId   { get; set; }
    public string  Name        { get; set; } = string.Empty;
    public string? Size        { get; set; }
    public int     Quantity    { get; set; }
    public decimal UnitPrice   { get; set; }
}

public class TStoreEvento
{
    public Guid     Id        { get; set; }
    public string   Type      { get; set; } = string.Empty;
    public int?     Phase     { get; set; }
    public string   Message   { get; set; } = string.Empty;
    public string   ActorName { get; set; } = string.Empty;
    public string   ActorRole { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class TStorePedido
{
    public Guid      Id               { get; set; }
    public int       OrderNumber      { get; set; }
    public Guid      UserId           { get; set; }
    public string    CustomerName     { get; set; } = string.Empty;
    public string    Email            { get; set; } = string.Empty;
    public string    Phone            { get; set; } = string.Empty;
    public string    Province         { get; set; } = string.Empty;
    public string?   Canton           { get; set; }
    public string?   District         { get; set; }
    public string    AddressLine      { get; set; } = string.Empty;
    public decimal   Subtotal         { get; set; }
    public decimal   ShippingCost     { get; set; }
    public decimal   Total            { get; set; }
    public string    PaymentMethod    { get; set; } = string.Empty;
    public string    PaymentStatus    { get; set; } = string.Empty;
    public string?   CardLastFour     { get; set; }
    public string?   CardNetwork      { get; set; }
    public int       FulfillmentPhase { get; set; }
    public bool      IsCancelled      { get; set; }
    public string?   CancelReason     { get; set; }
    public string?   CarrierName      { get; set; }
    public string?   TrackingNumber   { get; set; }
    public string?   PackageInfo      { get; set; }
    /// Solo se envía al dueño del pedido; el admin nunca lo recibe.
    public string?   DeliveryCode     { get; set; }
    public bool      HasOpenIncident  { get; set; }
    public string?   RescheduleWindow { get; set; }
    public DateTime? EtaFrom          { get; set; }
    public DateTime? EtaTo            { get; set; }
    public int?      Rating           { get; set; }
    public DateTime  CreatedAt        { get; set; }
    public List<TStoreItemPedido> Items  { get; set; } = [];
    public List<TStoreEvento>     Events { get; set; } = [];
}

public class TStoreNotificacion
{
    public Guid      Id        { get; set; }
    public string    Title     { get; set; } = string.Empty;
    public string    Body      { get; set; } = string.Empty;
    public Guid?     OrderId   { get; set; }
    public bool      Read      { get; set; }
    public DateTime  CreatedAt { get; set; }
}

public record TStoreActor(Guid? Id, string Role, string Name);
