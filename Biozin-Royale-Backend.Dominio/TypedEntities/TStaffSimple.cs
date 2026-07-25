namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TStaffSimple
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
