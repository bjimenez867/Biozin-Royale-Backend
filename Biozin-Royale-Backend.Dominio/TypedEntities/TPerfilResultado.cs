namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TPerfilResultado
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public DateOnly? Birthdate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Role { get; set; } = "user";
    public bool IsGuest { get; set; }
    public string? Token { get; set; }
    public List<string> CamposPendientes { get; set; } = new();
}
