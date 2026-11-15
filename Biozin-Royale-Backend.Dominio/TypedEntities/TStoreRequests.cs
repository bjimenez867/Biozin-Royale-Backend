namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TStoreLineaPedido
{
    public Guid    ProductId { get; set; }
    public string? Size      { get; set; }
    public int     Quantity  { get; set; }
}

public class TStoreCrearPedido
{
    /// "wallet" | "card"
    public string  PaymentMethod   { get; set; } = string.Empty;
    /// Obligatorio con "card": id de una tarjeta de payment_methods del usuario.
    public Guid?   PaymentMethodId { get; set; }
    public string  Phone           { get; set; } = string.Empty;
    public string  Province        { get; set; } = string.Empty;
    public string? Canton          { get; set; }
    public string? District        { get; set; }
    public string  AddressLine     { get; set; } = string.Empty;
    public List<TStoreLineaPedido> Items { get; set; } = [];
    public string? IdempotencyKey  { get; set; }
}

public class TStoreMotivo      { public string? Reason  { get; set; } }
public class TStorePaquete     { public string  Package { get; set; } = string.Empty; }
public class TStoreCodigo      { public string  Code    { get; set; } = string.Empty; }
public class TStoreVentana     { public string  Window  { get; set; } = string.Empty; }
public class TStoreCalificacion{ public int     Stars   { get; set; } }

public class TStoreDireccion
{
    public string  Province    { get; set; } = string.Empty;
    public string? Canton      { get; set; }
    public string? District    { get; set; }
    public string  AddressLine { get; set; } = string.Empty;
}

public class TStoreCrearSolicitud
{
    public Guid    OrderItemId  { get; set; }
    /// "exchange" | "refund"
    public string  RequestType  { get; set; } = string.Empty;
    public string  Reason       { get; set; } = string.Empty;
    public string? Detail       { get; set; }
    public int     Quantity     { get; set; }
    /// Obligatorio con "exchange": la talla que pide a cambio.
    public string? ExchangeSize { get; set; }
}

public class TStoreResolverSolicitud { public string? Note { get; set; } }

public class TStoreGuardarProducto
{
    public string  Name        { get; set; } = string.Empty;
    /// Nombre de la categoría (Camisetas, Hogar, ...).
    public string  Category    { get; set; } = string.Empty;
    public decimal Price       { get; set; }
    public string? Description { get; set; }
    /// Productos sin talla: stock total. Los productos con talla se ajustan por talla con AjusteStock.
    public int?    Stock       { get; set; }
    public bool?   IsVisible   { get; set; }
}

public class TStoreAjusteStock
{
    public int     Delta { get; set; }
    /// Talla a ajustar; null para productos sin talla.
    public string? Size  { get; set; }
}
