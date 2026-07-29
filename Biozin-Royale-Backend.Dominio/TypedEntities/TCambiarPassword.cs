namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TCambiarPassword
{
    public string OldPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
