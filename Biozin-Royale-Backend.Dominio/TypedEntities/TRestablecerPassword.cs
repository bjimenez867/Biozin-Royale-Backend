namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TRestablecerPassword
{
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
