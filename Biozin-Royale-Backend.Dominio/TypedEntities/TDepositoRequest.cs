namespace Biozin_Royale_Backend.Dominio.TypedEntities;

public class TDepositoRequest
{
    public decimal Amount { get; set; }
}

public class TCapturarPayPalRequest
{
    public string OrderId { get; set; } = "";
}
