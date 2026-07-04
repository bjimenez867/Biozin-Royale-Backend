namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TUserBlockInfo
{
    public Guid Id { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime BlockedAt { get; set; }
    public string BlockedByName { get; set; } = string.Empty;
}
