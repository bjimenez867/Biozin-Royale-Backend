namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TStoreVariante
{
    public Guid    Id    { get; set; }
    public string? Size  { get; set; }
    public int     Stock { get; set; }
}

public class TStoreProducto
{
    public Guid     Id           { get; set; }
    public string   Sku          { get; set; } = string.Empty;
    public string   Name         { get; set; } = string.Empty;
    public Guid     CategoryId   { get; set; }
    public string   Category     { get; set; } = string.Empty;
    public string   Description  { get; set; } = string.Empty;
    public decimal  Price        { get; set; }
    public string?  ImagePath    { get; set; }
    public string?  ArtType      { get; set; }
    public string?  ArtVariant   { get; set; }
    public string   Badge        { get; set; } = string.Empty;
    public bool     RequiresSize { get; set; }
    public bool     IsVisible    { get; set; }
    /// Suma del stock de las variantes activas.
    public int      Stock        { get; set; }
    public List<TStoreVariante> Variants { get; set; } = [];
}

public class TStoreConfig
{
    public decimal FreeShippingFrom { get; set; }
    public decimal ShippingCost     { get; set; }
    public string  DispatchCutoff   { get; set; } = string.Empty;
    public int     ReturnWindowDays { get; set; }
}
