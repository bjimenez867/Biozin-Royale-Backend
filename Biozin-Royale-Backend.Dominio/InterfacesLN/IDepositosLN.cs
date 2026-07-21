using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IDepositosLN
{
    Task<Response<TIniciarDepositoResultado>> IniciarStripeAsync(Guid userId, decimal amount);
    Task<Response<TIniciarDepositoResultado>> IniciarPayPalAsync(Guid userId, decimal amount);
    Task<Response<decimal>>                  CapturarPayPalAsync(Guid userId, string orderId);
    Task<Response<bool>>                     ProcesarWebhookStripeAsync(string payload, string signature);
}
