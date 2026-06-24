using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IBetsLN
{
    Task<Response<TBetResult>> PlaceBetAsync(Guid userId, TPlaceBetRequest request);
}
