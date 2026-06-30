namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TCrearStaffMember
{
    public string Nombre { get; set; } = string.Empty;
    public string CorreoContacto { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Role { get; set; } = string.Empty;
}
