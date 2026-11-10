namespace Biozin_Royale_Backend.Dominio.Entities;

public class StoreSetting
{
    public string   SettingKey   { get; set; } = string.Empty;
    public string   SettingValue { get; set; } = string.Empty;
    public string?  Description  { get; set; }
    public DateTime UpdatedAt    { get; set; }
}
