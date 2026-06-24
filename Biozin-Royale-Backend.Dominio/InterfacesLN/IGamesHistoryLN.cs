using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IGamesHistoryLN
{
    Task<Response<IEnumerable<TGamesHistory>>> ObtenerHistorialAsync(Guid userId);
}
