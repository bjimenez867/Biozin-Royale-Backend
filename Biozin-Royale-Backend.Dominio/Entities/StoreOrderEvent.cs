namespace Biozin_Royale_Backend.Dominio.Entities;

public class StoreOrderEvent
{
    public Guid     Id        { get; set; }
    public Guid     OrderId   { get; set; }
    public string   EventType { get; set; } = string.Empty;  // phase | payment | incident | reschedule | cancellation | change | rating
    public short?   Phase     { get; set; }
    public string   Message   { get; set; } = string.Empty;
    public Guid?    ActorId   { get; set; }
    public string   ActorRole { get; set; } = "system";      // user | authenticated | admin | system
    public string   ActorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
