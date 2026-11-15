using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IStoreLN
{
    // Catálogo
    Task<Response<List<TStoreProducto>>> ListarProductosAsync(bool incluirOcultos);
    Task<Response<TStoreConfig>> ObtenerConfigAsync();

    // Pedidos (cliente)
    Task<Response<TStorePedido>> CrearPedidoAsync(Guid userId, TStoreCrearPedido req);
    Task<Response<List<TStorePedido>>> ListarMisPedidosAsync(Guid userId);
    Task<Response<TStorePedido>> ObtenerPedidoAsync(Guid actorId, bool esAdmin, Guid orderId);
    Task<Response<TStorePedido>> CambiarDireccionAsync(Guid userId, Guid orderId, TStoreDireccion datos);
    Task<Response<TStorePedido>> CalificarAsync(Guid userId, Guid orderId, int estrellas);
    Task<Response<TStorePedido>> CrearSolicitudAsync(Guid userId, Guid orderId, TStoreCrearSolicitud req);

    // Cliente o admin
    Task<Response<TStorePedido>> CancelarAsync(Guid actorId, bool esAdmin, Guid orderId, string? motivo);
    Task<Response<TStorePedido>> ReprogramarAsync(Guid actorId, bool esAdmin, Guid orderId, string ventana);

    // Admin
    Task<Response<List<TStorePedido>>> ListarPedidosAdminAsync();
    Task<Response<TStorePedido>> ConfirmarPagoAsync(Guid adminId, Guid orderId);
    Task<Response<TStorePedido>> RechazarPagoAsync(Guid adminId, Guid orderId);
    Task<Response<TStorePedido>> IniciarPreparacionAsync(Guid adminId, Guid orderId);
    Task<Response<TStorePedido>> MarcarEmpacadoAsync(Guid adminId, Guid orderId, string paquete);
    Task<Response<TStorePedido>> DespacharAsync(Guid adminId, Guid orderId);
    Task<Response<TStorePedido>> IniciarRepartoAsync(Guid adminId, Guid orderId);
    Task<Response<TStorePedido>> RegistrarIncidenciaAsync(Guid adminId, Guid orderId, string motivo);
    Task<Response<TStorePedido>> ConfirmarEntregaAsync(Guid adminId, Guid orderId, string codigo);
    Task<Response<TStorePedido>> AprobarSolicitudAsync(Guid adminId, Guid solicitudId, string? nota);
    Task<Response<TStorePedido>> RechazarSolicitudAsync(Guid adminId, Guid solicitudId, string? nota);

    Task<Response<TStoreProducto>> CrearProductoAsync(TStoreGuardarProducto datos);
    Task<Response<TStoreProducto>> ActualizarProductoAsync(Guid id, TStoreGuardarProducto datos);
    Task<Response<bool>> EliminarProductoAsync(Guid id);
    Task<Response<TStoreProducto>> AjustarStockAsync(Guid id, TStoreAjusteStock datos);

    // Avisos del cliente
    Task<Response<List<TStoreNotificacion>>> ListarNotificacionesAsync(Guid userId);
    Task<Response<bool>> MarcarLeidaAsync(Guid userId, Guid id);
    Task<Response<bool>> MarcarTodasLeidasAsync(Guid userId);
}
