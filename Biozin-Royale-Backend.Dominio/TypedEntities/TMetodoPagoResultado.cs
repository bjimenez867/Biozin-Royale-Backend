namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TMetodoPagoResultado
{
    public Guid      Id           { get; set; }
    public string    Type         { get; set; } = string.Empty;  // "paypal" | "card"
    public string?   Alias        { get; set; }
    public bool      IsDefault    { get; set; }
    public DateTime  CreatedAt    { get; set; }

    // PayPal
    public string?   Email        { get; set; }

    // Tarjeta
    public string?   CardHolder   { get; set; }
    public string?   LastFour     { get; set; }
    public string?   Network      { get; set; }
    public int?      ExpiryMonth  { get; set; }
    public int?      ExpiryYear   { get; set; }
}

public class TAgregarPayPalRequest
{
    public string  Email     { get; set; } = string.Empty;
    public string? Alias     { get; set; }
    public bool    IsDefault { get; set; }
}

public class TAgregarTarjetaRequest
{
    public string  CardNumber   { get; set; } = string.Empty;   // sólo se usa para extraer los últimos 4 dígitos y la red
    public string  CardHolder   { get; set; } = string.Empty;
    public int     ExpiryMonth  { get; set; }
    public int     ExpiryYear   { get; set; }
    public string? Alias        { get; set; }
    public bool    IsDefault    { get; set; }
}
