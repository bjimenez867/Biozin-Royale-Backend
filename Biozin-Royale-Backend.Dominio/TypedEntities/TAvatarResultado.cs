namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TAvatarResultado
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}
