using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IInternalRequestsLN
{
    Task<Response<TInternalRequest>> CrearAsync(TCrearInternalRequest datos, Guid soporteId);
    Task<Response<IEnumerable<TInternalRequest>>> ListarMiasAsync(Guid soporteId);
    Task<Response<IEnumerable<TInternalRequest>>> ListarParaMiAsync();
    Task<Response<TInternalRequest>> ObtenerAsync(Guid id, Guid callerId, string callerRole);

    Task<Response<IEnumerable<TInternalRequestMessage>>> ListarMensajesAsync(Guid id, Guid callerId, string callerRole);
    Task<Response<TInternalRequestMessage>> EnviarMensajeAsync(Guid id, Guid senderId, string senderRole, TEnviarInternalRequestMensaje datos);
    Task<Response<TInternalRequest>> CambiarEstadoAsync(Guid id, string status);
    Task<Response<IEnumerable<TStaffSimple>>> ListarAdminsAsync();
}
