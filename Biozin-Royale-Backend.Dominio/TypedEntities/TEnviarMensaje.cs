namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TEnviarMensaje
{
    public string Body { get; set; } = string.Empty;
    public string? FileUrl { get; set; }
    public string? FileName { get; set; }
}
