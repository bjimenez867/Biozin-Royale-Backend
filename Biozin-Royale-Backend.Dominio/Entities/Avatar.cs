namespace Biozin_Royale_Backend.Dominio.Entities;

public class Avatar
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
