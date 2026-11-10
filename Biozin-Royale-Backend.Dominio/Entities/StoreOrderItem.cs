namespace Biozin_Royale_Backend.Dominio.Entities;

public class StoreOrderItem
{
    public Guid     Id          { get; set; }
    public Guid     OrderId     { get; set; }
    public Guid     ProductId   { get; set; }
    public Guid     VariantId   { get; set; }
    public string   ProductName { get; set; } = string.Empty;
    public string?  Size        { get; set; }
    public decimal  UnitPrice   { get; set; }
    public int      Quantity    { get; set; }
    public DateTime CreatedAt   { get; set; }
}
