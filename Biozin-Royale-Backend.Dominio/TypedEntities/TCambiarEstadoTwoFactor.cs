namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TCambiarEstadoTwoFactor
{
    public string Password { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}
