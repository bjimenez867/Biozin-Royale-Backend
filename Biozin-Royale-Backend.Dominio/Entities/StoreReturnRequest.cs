namespace Biozin_Royale_Backend.Dominio.Entities;

/// Cambio de talla o devolución de un artículo del pedido. Ver comentario de la tabla en
/// Extras/store-incidencias-devoluciones.sql.
public class StoreReturnRequest
{
    public Guid      Id                        { get; set; }
    public Guid      OrderId                   { get; set; }
    public Guid      OrderItemId               { get; set; }
    public string    RequestType               { get; set; } = string.Empty;   // exchange | refund
    public string    Reason                    { get; set; } = string.Empty;
    public string    Detail                    { get; set; } = string.Empty;
    public int       Quantity                  { get; set; }
    public string?   ExchangeSize              { get; set; }
    public string    Status                    { get; set; } = "pending";      // pending | approved | rejected
    public string?   ResolutionNote             { get; set; }
    public Guid?      RefundWalletTransactionId { get; set; }
    public Guid?      ResolvedById              { get; set; }
    public string?    ResolvedByName            { get; set; }
    public DateTime?  ResolvedAt                { get; set; }
    public DateTime   CreatedAt                 { get; set; }
    public DateTime   UpdatedAt                 { get; set; }
}
