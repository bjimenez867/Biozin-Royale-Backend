using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IRetirosLN
{
    Task<Response<TRetiroResultado>> ProcesarRetiroAsync(Guid userId, TIniciarRetiroRequest request);
}
