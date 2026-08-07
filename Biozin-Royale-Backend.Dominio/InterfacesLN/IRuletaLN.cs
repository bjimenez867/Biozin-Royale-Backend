using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IRuletaLN
{
    Task<Response<TRuletaSpinResult>> SpinAsync(Guid userId, Dictionary<string, decimal> bets);
}
