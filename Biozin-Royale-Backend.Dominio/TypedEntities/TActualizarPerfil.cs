namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TActualizarPerfil
{
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public DateOnly? Birthdate { get; set; }
}
