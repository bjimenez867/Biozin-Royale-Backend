namespace Biozin_Royale_Backend.Dominio.Entities;

public class StoreProduct
{
    public Guid      Id           { get; set; }
    public string    Sku          { get; set; } = string.Empty;
    public string    Name         { get; set; } = string.Empty;
    public Guid      CategoryId   { get; set; }
    public string    Description  { get; set; } = string.Empty;
    public decimal   Price        { get; set; }
    public string?   ImagePath    { get; set; }
    public string?   ArtType      { get; set; }
    public string?   ArtVariant   { get; set; }
    public string    Badge        { get; set; } = string.Empty;
    public bool      RequiresSize { get; set; }
    public bool      IsVisible    { get; set; } = true;
    public DateTime? DeletedAt    { get; set; }
    public DateTime  CreatedAt    { get; set; }
    public DateTime  UpdatedAt    { get; set; }
}
