namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TCrearInternalRequest
{
    public string Subject { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid TargetAdminId { get; set; }
}
