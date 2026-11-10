namespace Biozin_Royale_Backend.Dominio.Entities;

public class StoreProductVariant
{
    public Guid     Id           { get; set; }
    public Guid     ProductId    { get; set; }
    public string?  Size         { get; set; }
    public string   Sku          { get; set; } = string.Empty;
    public int      Stock        { get; set; }
    public int      ReorderLevel { get; set; } = 5;
    public bool     IsActive     { get; set; } = true;
    public DateTime CreatedAt    { get; set; }
    public DateTime UpdatedAt    { get; set; }
}
