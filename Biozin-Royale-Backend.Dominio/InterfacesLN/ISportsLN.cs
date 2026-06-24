using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface ISportsLN
{
    Task<Response<IEnumerable<TSportsMatch>>> GetMatchesAsync();
}
