using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IMetodosPagoLN
{
    Response<IEnumerable<TMetodoPagoResultado>> Listar(Guid userId);
    Response<TMetodoPagoResultado>              AgregarPayPal(Guid userId, TAgregarPayPalRequest request);
    Response<TMetodoPagoResultado>              AgregarTarjeta(Guid userId, TAgregarTarjetaRequest request);
    Response<bool>                              Eliminar(Guid userId, Guid methodId);
    Response<bool>                              EstablecerPrincipal(Guid userId, Guid methodId);
}
