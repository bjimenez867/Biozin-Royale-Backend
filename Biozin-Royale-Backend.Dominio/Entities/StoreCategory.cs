namespace Biozin_Royale_Backend.Dominio.Entities;

public class StoreCategory
{
    public Guid     Id          { get; set; }
    public string   Name        { get; set; } = string.Empty;
    public string   Slug        { get; set; } = string.Empty;
    public string?  Description { get; set; }
    public int      SortOrder   { get; set; }
    public bool     IsActive    { get; set; } = true;
    public DateTime CreatedAt   { get; set; }
    public DateTime UpdatedAt   { get; set; }
}
