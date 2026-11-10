using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Biozin_Royale_Backend.API.Hubs;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.API.Controllers;

/// Tienda. Las rutas de cliente rechazan al staff (el admin no compra); las de gestión exigen rol admin.
[Authorize]
[ApiController]
[Route("api/tienda")]
public class StoreController : ControllerBase
{
    private readonly IStoreLN _store;
    private readonly IHubContext<ChatHub> _hub;

    public StoreController(IStoreLN store, IHubContext<ChatHub> hub)
    {
        _store = store;
        _hub = hub;
    }

    private Guid ActorId => Guid.Parse(User.FindFirst("sub")!.Value);
    private bool EsAdmin => User.FindFirst("role")?.Value == "admin";
    private bool EsStaff => User.FindFirst("role")?.Value is "admin" or "soporte";

    // ── Catálogo ──────────────────────────────────────────────────────────

    [HttpGet("productos")]
    public async Task<IActionResult> Productos()
    {
        if (User.FindFirst("role")?.Value == "soporte") return Forbid();
        return Ok(await _store.ListarProductosAsync(incluirOcultos: EsAdmin));
    }

    [HttpGet("config")]
    public async Task<IActionResult> Config()
    {
        if (User.FindFirst("role")?.Value == "soporte") return Forbid();
        return Ok(await _store.ObtenerConfigAsync());
    }

    // ── Cliente ───────────────────────────────────────────────────────────

    [HttpPost("pedidos")]
    [EnableRateLimiting("payments")]
    public async Task<IActionResult> Crear([FromBody] TStoreCrearPedido datos)
    {
        if (EsStaff) return Forbid();
        return await Responder(await _store.CrearPedidoAsync(ActorId, datos));
    }

    [HttpGet("pedidos/mios")]
    public async Task<IActionResult> Mios()
    {
        if (EsStaff) return Forbid();
        return Ok(await _store.ListarMisPedidosAsync(ActorId));
    }

    [HttpGet("pedidos/{id:guid}")]
    public async Task<IActionResult> Obtener(Guid id)
    {
        if (User.FindFirst("role")?.Value == "soporte") return Forbid();
        var r = await _store.ObtenerPedidoAsync(ActorId, EsAdmin, id);
        return r.blnError ? NotFound(r) : Ok(r);
    }

    [HttpPut("pedidos/{id:guid}/direccion")]
    public async Task<IActionResult> CambiarDireccion(Guid id, [FromBody] TStoreDireccion datos)
    {
        if (EsStaff) return Forbid();
        return await Responder(await _store.CambiarDireccionAsync(ActorId, id, datos));
    }

    [HttpPost("pedidos/{id:guid}/calificar")]
    public async Task<IActionResult> Calificar(Guid id, [FromBody] TStoreCalificacion datos)
    {
        if (EsStaff) return Forbid();
        return await Responder(await _store.CalificarAsync(ActorId, id, datos.Stars));
    }

    [HttpPost("pedidos/{id:guid}/cambio")]
    public async Task<IActionResult> SolicitarCambio(Guid id)
    {
        if (EsStaff) return Forbid();
        return await Responder(await _store.SolicitarCambioAsync(ActorId, id));
    }

    // ── Cliente o admin ───────────────────────────────────────────────────

    [HttpPost("pedidos/{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id, [FromBody] TStoreMotivo? datos)
    {
        if (User.FindFirst("role")?.Value == "soporte") return Forbid();
        return await Responder(await _store.CancelarAsync(ActorId, EsAdmin, id, datos?.Reason));
    }

    [HttpPost("pedidos/{id:guid}/reprogramar")]
    public async Task<IActionResult> Reprogramar(Guid id, [FromBody] TStoreVentana datos)
    {
        if (User.FindFirst("role")?.Value == "soporte") return Forbid();
        return await Responder(await _store.ReprogramarAsync(ActorId, EsAdmin, id, datos.Window));
    }

    // ── Avisos del cliente ────────────────────────────────────────────────

    [HttpGet("notificaciones")]
    public async Task<IActionResult> Notificaciones()
    {
        if (EsStaff) return Forbid();
        return Ok(await _store.ListarNotificacionesAsync(ActorId));
    }

    [HttpPost("notificaciones/{id:guid}/leida")]
    public async Task<IActionResult> MarcarLeida(Guid id)
    {
        if (EsStaff) return Forbid();
        return Ok(await _store.MarcarLeidaAsync(ActorId, id));
    }

    [HttpPost("notificaciones/leidas")]
    public async Task<IActionResult> MarcarTodasLeidas()
    {
        if (EsStaff) return Forbid();
        return Ok(await _store.MarcarTodasLeidasAsync(ActorId));
    }

    // ── Admin ─────────────────────────────────────────────────────────────

    [Authorize(Roles = "admin")]
    [HttpGet("admin/pedidos")]
    public async Task<IActionResult> TodosLosPedidos() =>
        Ok(await _store.ListarPedidosAdminAsync());

    [Authorize(Roles = "admin")]
    [HttpPost("admin/pedidos/{id:guid}/confirmar-pago")]
    public async Task<IActionResult> ConfirmarPago(Guid id) =>
        await Responder(await _store.ConfirmarPagoAsync(ActorId, id));

    [Authorize(Roles = "admin")]
    [HttpPost("admin/pedidos/{id:guid}/rechazar-pago")]
    public async Task<IActionResult> RechazarPago(Guid id) =>
        await Responder(await _store.RechazarPagoAsync(ActorId, id));

    [Authorize(Roles = "admin")]
    [HttpPost("admin/pedidos/{id:guid}/preparar")]
    public async Task<IActionResult> Preparar(Guid id) =>
        await Responder(await _store.IniciarPreparacionAsync(ActorId, id));

    [Authorize(Roles = "admin")]
    [HttpPost("admin/pedidos/{id:guid}/empacar")]
    public async Task<IActionResult> Empacar(Guid id, [FromBody] TStorePaquete datos) =>
        await Responder(await _store.MarcarEmpacadoAsync(ActorId, id, datos.Package));

    [Authorize(Roles = "admin")]
    [HttpPost("admin/pedidos/{id:guid}/despachar")]
    public async Task<IActionResult> Despachar(Guid id) =>
        await Responder(await _store.DespacharAsync(ActorId, id));

    [Authorize(Roles = "admin")]
    [HttpPost("admin/pedidos/{id:guid}/reparto")]
    public async Task<IActionResult> Reparto(Guid id) =>
        await Responder(await _store.IniciarRepartoAsync(ActorId, id));

    [Authorize(Roles = "admin")]
    [HttpPost("admin/pedidos/{id:guid}/incidencia")]
    public async Task<IActionResult> Incidencia(Guid id, [FromBody] TStoreMotivo datos) =>
        await Responder(await _store.RegistrarIncidenciaAsync(ActorId, id, datos.Reason ?? ""));

    [Authorize(Roles = "admin")]
    [HttpPost("admin/pedidos/{id:guid}/entregar")]
    public async Task<IActionResult> Entregar(Guid id, [FromBody] TStoreCodigo datos) =>
        await Responder(await _store.ConfirmarEntregaAsync(ActorId, id, datos.Code));

    // ── Admin: productos ──────────────────────────────────────────────────

    [Authorize(Roles = "admin")]
    [HttpPost("admin/productos")]
    public async Task<IActionResult> CrearProducto([FromBody] TStoreGuardarProducto datos)
    {
        var r = await _store.CrearProductoAsync(datos);
        return r.blnError ? BadRequest(r) : Ok(r);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("admin/productos/{id:guid}")]
    public async Task<IActionResult> ActualizarProducto(Guid id, [FromBody] TStoreGuardarProducto datos)
    {
        var r = await _store.ActualizarProductoAsync(id, datos);
        return r.blnError ? BadRequest(r) : Ok(r);
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("admin/productos/{id:guid}")]
    public async Task<IActionResult> EliminarProducto(Guid id)
    {
        var r = await _store.EliminarProductoAsync(id);
        return r.blnError ? BadRequest(r) : Ok(r);
    }

    [Authorize(Roles = "admin")]
    [HttpPost("admin/productos/{id:guid}/stock")]
    public async Task<IActionResult> AjustarStock(Guid id, [FromBody] TStoreAjusteStock datos)
    {
        var r = await _store.AjustarStockAsync(id, datos);
        return r.blnError ? BadRequest(r) : Ok(r);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// Tras cada cambio avisa por SignalR (solo ids, nunca el código de entrega) al dueño del pedido
    /// y al staff, para que ambas pantallas se refresquen sin sondear.
    private async Task<IActionResult> Responder(Response<TStorePedido> r)
    {
        if (r.blnError) return BadRequest(r);

        var aviso = new { id = r.ReturnValue.Id, orderNumber = r.ReturnValue.OrderNumber };
        await _hub.Clients.Group(ChatHub.UserNotificationsGroup(r.ReturnValue.UserId)).SendAsync("pedidoTienda", aviso);
        await _hub.Clients.Group(ChatHub.StaffNotificationsGroup).SendAsync("pedidoTienda", aviso);
        return Ok(r);
    }
}
