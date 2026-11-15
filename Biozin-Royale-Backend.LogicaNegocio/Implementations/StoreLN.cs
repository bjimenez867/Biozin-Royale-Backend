using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

/// Lógica de la tienda. Solo escribe en tablas store_* y, para pagar/reembolsar con
/// billetera, en wallets y wallet_transactions (transaction_type purchase / purchase_refund),
/// que ningún otro módulo lee. Las operaciones que no deben repetirse (stock, saldo, cambios de
/// etapa, cancelación) se hacen con UPDATE condicionado dentro de una transacción.
public class StoreLN : IStoreLN
{
    private readonly IUnitWork _uow;

    private const string Transportista = "Correos de Costa Rica";
    private static readonly string[] Provincias = ["San José", "Alajuela", "Cartago", "Heredia", "Guanacaste", "Puntarenas", "Limón"];
    private static readonly TStoreActor Sistema = new(null, "system", "Sistema");

    public StoreLN(IUnitWork uow) => _uow = uow;

    // ═══════════════════════════ Catálogo ═══════════════════════════

    public async Task<Response<List<TStoreProducto>>> ListarProductosAsync(bool incluirOcultos)
    {
        var r = new Response<List<TStoreProducto>>();

        var productos = await _uow.StoreProducts.ObtenerEntidadesAsync(p =>
            p.DeletedAt == null && (incluirOcultos || p.IsVisible));
        var ids = productos.Select(p => p.Id).ToList();
        var variantes = await _uow.StoreProductVariants.ObtenerEntidadesAsync(v => ids.Contains(v.ProductId) && v.IsActive);
        var categorias = (await _uow.StoreCategories.ListarAsync()).ToDictionary(c => c.Id, c => c);

        r.ReturnValue = productos
            .OrderBy(p => categorias.TryGetValue(p.CategoryId, out var c) ? c.SortOrder : 99)
            .ThenBy(p => p.Name)
            .Select(p =>
            {
                var vs = variantes.Where(v => v.ProductId == p.Id)
                    .OrderBy(v => TallaOrden(v.Size)).ToList();
                return new TStoreProducto
                {
                    Id = p.Id, Sku = p.Sku, Name = p.Name, CategoryId = p.CategoryId,
                    Category = categorias.TryGetValue(p.CategoryId, out var c) ? c.Name : "",
                    Description = p.Description, Price = p.Price, ImagePath = p.ImagePath,
                    ArtType = p.ArtType, ArtVariant = p.ArtVariant, Badge = p.Badge,
                    RequiresSize = p.RequiresSize, IsVisible = p.IsVisible,
                    Stock = vs.Sum(v => v.Stock),
                    Variants = vs.Select(v => new TStoreVariante { Id = v.Id, Size = v.Size, Stock = v.Stock }).ToList(),
                };
            }).ToList();
        return r;
    }

    public async Task<Response<TStoreConfig>> ObtenerConfigAsync()
    {
        var a = await CargarAjustesAsync();
        return new Response<TStoreConfig>
        {
            ReturnValue = new TStoreConfig
            {
                FreeShippingFrom = Dec(a, "free_shipping_from", 60),
                ShippingCost = Dec(a, "shipping_cost", 5),
                ReturnWindowDays = Int(a, "return_window_days", 15),
                MaxDeliveryAttempts = Int(a, "max_delivery_attempts", 3),
            },
        };
    }

    // ═══════════════════════════ Crear pedido ═══════════════════════════

    public async Task<Response<TStorePedido>> CrearPedidoAsync(Guid userId, TStoreCrearPedido req)
    {
        var r = new Response<TStorePedido>();

        // Reintento de la misma compra: devuelve el pedido ya creado.
        var key = string.IsNullOrWhiteSpace(req.IdempotencyKey) ? null : req.IdempotencyKey.Trim();
        if (key is { Length: > 64 }) { r.lpError("Pedido", "Clave de operación inválida."); return r; }
        if (key is not null)
        {
            var previo = await _uow.StoreOrders.ObtenerEntidadAsync(o => o.UserId == userId && o.IdempotencyKey == key);
            if (previo is not null) return await DtoAsync(previo.Id, userId);
        }

        // ── Validaciones de entrada ──
        var metodo = (req.PaymentMethod ?? "").Trim().ToLowerInvariant();
        if (metodo is not ("wallet" or "card")) { r.lpError("Pago", "Elige un método de pago válido."); return r; }

        var phone = (req.Phone ?? "").Trim();
        var digitos = phone.Count(char.IsDigit);
        if (digitos < 8 || digitos > 15 || phone.Length > 20) { r.lpError("Teléfono", "Ingresa un teléfono válido."); return r; }

        var provincia = (req.Province ?? "").Trim();
        if (!Provincias.Contains(provincia)) { r.lpError("Dirección", "Elige una provincia válida."); return r; }
        var direccion = (req.AddressLine ?? "").Trim();
        if (direccion.Length < 5 || direccion.Length > 300) { r.lpError("Dirección", "Ingresa una dirección de entrega completa."); return r; }
        var canton = NullSiVacio(req.Canton, 60);
        var distrito = NullSiVacio(req.District, 60);

        if (req.Items is null || req.Items.Count == 0 || req.Items.Count > 50) { r.lpError("Carrito", "Tu carrito está vacío."); return r; }
        if (req.Items.Any(i => i.Quantity < 1 || i.Quantity > 50)) { r.lpError("Carrito", "Cantidad inválida."); return r; }

        var pedidas = req.Items
            .GroupBy(i => (i.ProductId, Size: string.IsNullOrWhiteSpace(i.Size) ? null : i.Size.Trim().ToUpperInvariant()))
            .Select(g => (g.Key.ProductId, g.Key.Size, Qty: g.Sum(x => x.Quantity)))
            .ToList();
        if (pedidas.Any(l => l.Qty > 50)) { r.lpError("Carrito", "Cantidad inválida."); return r; }

        // ── Productos y variantes ──
        var productoIds = pedidas.Select(l => l.ProductId).Distinct().ToList();
        var productos = await _uow.StoreProducts.ObtenerEntidadesAsync(p => productoIds.Contains(p.Id) && p.IsVisible && p.DeletedAt == null);
        var variantes = await _uow.StoreProductVariants.ObtenerEntidadesAsync(v => productoIds.Contains(v.ProductId) && v.IsActive);

        var lineas = new List<(StoreProduct P, StoreProductVariant V, int Qty)>();
        foreach (var l in pedidas)
        {
            var p = productos.FirstOrDefault(x => x.Id == l.ProductId);
            if (p is null) { r.lpError("Carrito", "Un producto de tu carrito ya no está disponible."); return r; }
            if (p.RequiresSize && l.Size is null) { r.lpError("Talla", $"Elige una talla para {p.Name}."); return r; }
            var v = variantes.FirstOrDefault(x => x.ProductId == p.Id &&
                (p.RequiresSize ? string.Equals(x.Size, l.Size, StringComparison.OrdinalIgnoreCase) : x.Size == null));
            if (v is null) { r.lpError("Talla", $"{p.Name} no está disponible en esa talla."); return r; }
            lineas.Add((p, v, l.Qty));
        }

        // ── Totales (siempre calculados aquí; el cliente no manda precios ni envío) ──
        var ajustes = await CargarAjustesAsync();
        var subtotal = lineas.Sum(l => l.P.Price * l.Qty);
        var envio = subtotal >= Dec(ajustes, "free_shipping_from", 60) ? 0m : Dec(ajustes, "shipping_cost", 5);
        var total = subtotal + envio;

        // ── Perfil y medio de pago ──
        var perfil = await _uow.Profiles.ObtenerEntidadAsync(p => p.UserId == userId);
        if (perfil is null) { r.lpError("Perfil", "No se encontró tu perfil."); return r; }
        var wallet = await _uow.Wallets.ObtenerEntidadAsync(w => w.UserId == userId);
        if (wallet is null) { r.lpError("Billetera", "No se encontró tu billetera."); return r; }

        PaymentMethod? tarjeta = null;
        if (metodo == "card")
        {
            if (req.PaymentMethodId is null) { r.lpError("Pago", "Elige una de tus tarjetas registradas."); return r; }
            tarjeta = await _uow.PaymentMethods.ObtenerEntidadAsync(m => m.Id == req.PaymentMethodId && m.WalletId == wallet.Id && m.Type == "card");
            if (tarjeta is null) { r.lpError("Pago", "No se encontró esa tarjeta."); return r; }
            var hoy = DateTime.UtcNow;
            if (tarjeta.ExpiryYear < hoy.Year || (tarjeta.ExpiryYear == hoy.Year && tarjeta.ExpiryMonth < hoy.Month))
            { r.lpError("Pago", "Esa tarjeta está vencida."); return r; }
        }
        else if (wallet.Balance < total)
        {
            r.lpError("Saldo insuficiente", "No tienes saldo suficiente en tu billetera."); return r;
        }

        var cliente = Trunc(perfil.DisplayName ?? perfil.Username, 120);
        var actorCliente = new TStoreActor(userId, "user", cliente);
        var ahora = DateTime.UtcNow;
        var hoyCr = HoyCostaRica();
        var order = new StoreOrder
        {
            Id = Guid.NewGuid(), UserId = userId, CustomerName = cliente, Phone = phone,
            Province = provincia, Canton = canton, District = distrito, AddressLine = direccion,
            Subtotal = subtotal, ShippingCost = envio, Total = total,
            PaymentStatus = "pending", FulfillmentPhase = 0,
            DeliveryCode = RandomNumberGenerator.GetInt32(0, 10000).ToString("D4"),
            EtaFrom = SumarHabiles(hoyCr, 2), EtaTo = SumarHabiles(hoyCr, 3),
            IdempotencyKey = key, CreatedAt = ahora, UpdatedAt = ahora,
        };

        await using var tx = await _uow.IniciarTransaccionAsync();
        try
        {
            // 1) Stock: descuento atómico; si otra compra se llevó las unidades, no se vende de más.
            foreach (var l in lineas)
            {
                var filas = await _uow.EjecutarSqlAsync(
                    $"UPDATE store_product_variants SET stock = stock - {l.Qty}, updated_at = now() WHERE id = {l.V.Id} AND is_active AND stock >= {l.Qty}");
                if (filas == 0)
                {
                    await tx.RollbackAsync();
                    r.lpError("Sin stock", $"No hay suficientes unidades de {l.P.Name}{(l.V.Size is null ? "" : " talla " + l.V.Size)}.");
                    return r;
                }
            }

            // 2) Pedido y artículos
            _uow.StoreOrders.Insertar(order);
            await _uow.CompletarAsync();   // asigna order_number
            foreach (var l in lineas)
                _uow.StoreOrderItems.Insertar(new StoreOrderItem
                {
                    Id = Guid.NewGuid(), OrderId = order.Id, ProductId = l.P.Id, VariantId = l.V.Id,
                    ProductName = l.P.Name, Size = l.V.Size, UnitPrice = l.P.Price, Quantity = l.Qty, CreatedAt = ahora,
                });
            await _uow.CompletarAsync();

            // 3) Pago
            var pago = new StorePayment
            {
                Id = Guid.NewGuid(), OrderId = order.Id, Method = metodo, Status = "pending",
                Amount = total, CreatedAt = ahora, UpdatedAt = ahora,
            };
            var confirmado = false;
            string metodoTexto;
            if (metodo == "wallet")
            {
                var debitadas = await _uow.EjecutarSqlAsync(
                    $"UPDATE wallets SET balance = balance - {total}, updated_at = now() WHERE id = {wallet.Id} AND balance >= {total}");
                if (debitadas == 0)
                {
                    await tx.RollbackAsync();
                    r.lpError("Saldo insuficiente", "No tienes saldo suficiente en tu billetera.");
                    return r;
                }
                var despues = (await _uow.Wallets.ObtenerEntidadAsync(w => w.Id == wallet.Id))!.Balance;
                var wtx = new WalletTransaction
                {
                    Id = Guid.NewGuid(), WalletId = wallet.Id, TransactionType = "purchase", Status = "completed",
                    Amount = total, BalanceBefore = despues + total, BalanceAfter = despues,
                    ReferenceType = "store_order", ReferenceId = order.Id,
                    ReceiptNumber = ReceiptGenerator.Generate(), CreatedAt = ahora,
                };
                _uow.WalletTransactions.Insertar(wtx);
                await _uow.CompletarAsync();
                pago.WalletTransactionId = wtx.Id;
                confirmado = true;
                metodoTexto = "Billetera";
            }
            else
            {
                pago.PaymentMethodId = tarjeta!.Id;
                pago.CardLastFour = tarjeta.LastFour;
                pago.CardNetwork = tarjeta.Network;
                confirmado = Bool(ajustes, "card_auto_confirm", false);
                metodoTexto = $"Tarjeta •••• {tarjeta.LastFour}";
            }

            if (confirmado)
            {
                pago.Status = "confirmed";
                pago.VerifiedAt = ahora;
                pago.VerifiedByName = "Sistema";
            }
            _uow.StorePayments.Insertar(pago);
            await _uow.CompletarAsync();

            if (confirmado)
                await _uow.EjecutarSqlAsync(
                    $"UPDATE store_orders SET payment_status = 'confirmed', fulfillment_phase = 1, updated_at = now() WHERE id = {order.Id}");

            // 4) Bitácora y aviso
            Evento(order.Id, "phase", 0, $"Pedido creado · {metodoTexto}", actorCliente);
            if (confirmado) Evento(order.Id, "payment", 1, "Pago confirmado automáticamente", Sistema);
            Aviso(userId, "Pedido recibido",
                confirmado ? $"Tu pedido {Codigo(order)} está confirmado." : $"Tu pedido {Codigo(order)} espera la verificación del pago.", order.Id);
            await _uow.CompletarAsync();

            await tx.CommitAsync();
        }
        catch (Exception) when (key is not null)
        {
            // Dos envíos simultáneos de la misma compra: el segundo choca con el índice único.
            await tx.RollbackAsync();
            var previo = await _uow.StoreOrders.ObtenerEntidadAsync(o => o.UserId == userId && o.IdempotencyKey == key);
            if (previo is null) throw;
            return await DtoAsync(previo.Id, userId);
        }

        return await DtoAsync(order.Id, userId);
    }

    // ═══════════════════════════ Consultas ═══════════════════════════

    public async Task<Response<List<TStorePedido>>> ListarMisPedidosAsync(Guid userId)
    {
        var ordenes = (await _uow.StoreOrders.ObtenerEntidadesAsync(o => o.UserId == userId))
            .OrderByDescending(o => o.CreatedAt).Take(200).ToList();
        return new Response<List<TStorePedido>> { ReturnValue = await ConstruirAsync(ordenes, userId) };
    }

    public async Task<Response<List<TStorePedido>>> ListarPedidosAdminAsync()
    {
        var ordenes = (await _uow.StoreOrders.ListarAsync())
            .OrderByDescending(o => o.CreatedAt).Take(500).ToList();
        return new Response<List<TStorePedido>> { ReturnValue = await ConstruirAsync(ordenes, null) };
    }

    public async Task<Response<TStorePedido>> ObtenerPedidoAsync(Guid actorId, bool esAdmin, Guid orderId)
    {
        var o = await PedidoAccesibleAsync(actorId, esAdmin, orderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        return await DtoAsync(orderId, esAdmin ? null : actorId);
    }

    // ═══════════════════════════ Acciones del cliente ═══════════════════════════

    public async Task<Response<TStorePedido>> CambiarDireccionAsync(Guid userId, Guid orderId, TStoreDireccion d)
    {
        var r = new Response<TStorePedido>();
        var o = await PedidoAccesibleAsync(userId, false, orderId);
        if (o is null) return NoEncontrado<TStorePedido>();

        var provincia = (d.Province ?? "").Trim();
        var direccion = (d.AddressLine ?? "").Trim();
        if (!Provincias.Contains(provincia)) { r.lpError("Dirección", "Elige una provincia válida."); return r; }
        if (direccion.Length < 5 || direccion.Length > 300) { r.lpError("Dirección", "Ingresa una dirección de entrega completa."); return r; }
        var canton = NullSiVacio(d.Canton, 60);
        var distrito = NullSiVacio(d.District, 60);
        var actor = await ActorAsync(userId, false);

        return await EnTransaccionAsync(async () =>
        {
            var filas = await _uow.EjecutarSqlAsync(
                $@"UPDATE store_orders SET province = {provincia}, canton = {canton}, district = {distrito}, address_line = {direccion}, updated_at = now()
                   WHERE id = {orderId} AND user_id = {userId} AND is_cancelled = false AND fulfillment_phase <= 2");
            if (filas == 0) { r.lpError("Pedido", "Ya no se puede cambiar la dirección de este pedido."); return r; }
            Evento(orderId, "change", null, $"Dirección actualizada · {provincia}, {direccion}", actor);
            await _uow.CompletarAsync();
            return await DtoAsync(orderId, userId);
        });
    }

    public async Task<Response<TStorePedido>> CalificarAsync(Guid userId, Guid orderId, int estrellas)
    {
        var r = new Response<TStorePedido>();
        if (estrellas < 1 || estrellas > 5) { r.lpError("Calificación", "La calificación debe ser de 1 a 5."); return r; }
        var o = await PedidoAccesibleAsync(userId, false, orderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        var actor = await ActorAsync(userId, false);
        var valor = (short)estrellas;

        return await EnTransaccionAsync(async () =>
        {
            var filas = await _uow.EjecutarSqlAsync(
                $@"UPDATE store_orders SET rating = {valor}, rated_at = now(), updated_at = now()
                   WHERE id = {orderId} AND user_id = {userId} AND fulfillment_phase = 6 AND is_cancelled = false AND rating IS NULL");
            if (filas == 0) { r.lpError("Pedido", "Solo puedes calificar una vez un pedido entregado."); return r; }
            Evento(orderId, "rating", null, $"Calificó la entrega con {estrellas} {(estrellas == 1 ? "estrella" : "estrellas")}", actor);
            await _uow.CompletarAsync();
            return await DtoAsync(orderId, userId);
        });
    }

    public async Task<Response<TStorePedido>> CrearSolicitudAsync(Guid userId, Guid orderId, TStoreCrearSolicitud req)
    {
        var r = new Response<TStorePedido>();
        var o = await PedidoAccesibleAsync(userId, false, orderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        if (o.IsCancelled || o.FulfillmentPhase != 6) { r.lpError("Pedido", "Solo puedes pedir un cambio de un pedido entregado."); return r; }

        var eventos = await _uow.StoreOrderEvents.ObtenerEntidadesAsync(e => e.OrderId == orderId);
        var entrega = eventos.Where(e => e.EventType == "phase" && e.Phase == 6).Select(e => (DateTime?)e.CreatedAt).Max();
        var ventanaDias = Int(await CargarAjustesAsync(), "return_window_days", 15);
        if (entrega is not null && DateTime.UtcNow > entrega.Value.AddDays(ventanaDias))
        { r.lpError("Cambio", $"El plazo para pedir un cambio es de {ventanaDias} días."); return r; }

        var tipo = (req.RequestType ?? "").Trim().ToLowerInvariant();
        if (tipo is not ("exchange" or "refund")) { r.lpError("Solicitud", "Elige si quieres un cambio de talla o una devolución."); return r; }
        var motivo = Trunc((req.Reason ?? "").Trim(), 40);
        if (motivo.Length < 3) { r.lpError("Solicitud", "Elige el motivo."); return r; }
        var detalle = Trunc((req.Detail ?? "").Trim(), 300);

        var item = await _uow.StoreOrderItems.ObtenerEntidadAsync(i => i.Id == req.OrderItemId && i.OrderId == orderId);
        if (item is null) { r.lpError("Solicitud", "Ese artículo no pertenece a este pedido."); return r; }
        if (req.Quantity < 1 || req.Quantity > item.Quantity) { r.lpError("Solicitud", "Cantidad inválida."); return r; }

        string? tallaNueva = null;
        if (tipo == "exchange")
        {
            if (item.Size is null) { r.lpError("Solicitud", "Este artículo no tiene talla para cambiar."); return r; }
            tallaNueva = (req.ExchangeSize ?? "").Trim().ToUpperInvariant();
            if (tallaNueva.Length == 0) { r.lpError("Solicitud", "Elige la talla que quieres a cambio."); return r; }
            if (tallaNueva == item.Size) { r.lpError("Solicitud", "Elige una talla distinta a la que compraste."); return r; }
        }

        var activa = await _uow.StoreReturnRequests.ObtenerEntidadAsync(
            s => s.OrderItemId == item.Id && (s.Status == "pending" || s.Status == "approved"));
        if (activa is not null) { r.lpError("Solicitud", "Ya hay una solicitud activa para este artículo."); return r; }

        var actor = await ActorAsync(userId, false);
        return await EnTransaccionAsync(async () =>
        {
            _uow.StoreReturnRequests.Insertar(new StoreReturnRequest
            {
                Id = Guid.NewGuid(), OrderId = orderId, OrderItemId = item.Id, RequestType = tipo, Reason = motivo,
                Detail = detalle, Quantity = req.Quantity, ExchangeSize = tallaNueva, Status = "pending",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            var talla = item.Size is null ? "" : $" ({item.Size})";
            var resumen = tipo == "exchange"
                ? $"Pidió cambio de talla · {req.Quantity}× {item.ProductName} · {item.Size} → {tallaNueva} · {motivo}"
                : $"Pidió devolución · {req.Quantity}× {item.ProductName}{talla} · {motivo}";
            Evento(orderId, "change", null, resumen, actor);
            await _uow.CompletarAsync();
            return await DtoAsync(orderId, userId);
        });
    }

    public async Task<Response<TStorePedido>> AprobarSolicitudAsync(Guid adminId, Guid solicitudId, string? nota)
    {
        var r = new Response<TStorePedido>();
        var s = await _uow.StoreReturnRequests.ObtenerEntidadAsync(x => x.Id == solicitudId);
        if (s is null) { r.lpError("Solicitud", "Solicitud no encontrada."); return r; }
        var o = await _uow.StoreOrders.ObtenerEntidadAsync(x => x.Id == s.OrderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        var item = await _uow.StoreOrderItems.ObtenerEntidadAsync(x => x.Id == s.OrderItemId);
        if (item is null) { r.lpError("Solicitud", "El artículo de esta solicitud ya no existe."); return r; }
        var actor = await ActorAsync(adminId, true);
        var notaTexto = NullSiVacio(nota, 300);

        return await EnTransaccionAsync(async () =>
        {
            var filas = await _uow.EjecutarSqlAsync(
                $@"UPDATE store_return_requests SET status = 'approved', resolution_note = {notaTexto},
                       resolved_by_id = {adminId}, resolved_by_name = {actor.Name}, resolved_at = now(), updated_at = now()
                   WHERE id = {solicitudId} AND status = 'pending'");
            if (filas == 0) { r.lpError("Solicitud", "Esta solicitud ya fue resuelta."); return r; }

            // El artículo vuelve al inventario: va a reponerse (cambio) o ya no va a salir de nuevo (devolución).
            await _uow.EjecutarSqlAsync($"UPDATE store_product_variants SET stock = stock + {s.Quantity}, updated_at = now() WHERE id = {item.VariantId}");

            string detalle, avisoTitulo, avisoCuerpo;
            if (s.RequestType == "exchange")
            {
                detalle = $"Cambio de talla aprobado · {item.ProductName} · {item.Size} → {s.ExchangeSize}" + (notaTexto is null ? "" : $" · {notaTexto}");
                avisoTitulo = "Cambio aprobado";
                avisoCuerpo = $"Vamos a coordinar el envío de tu {item.ProductName} talla {s.ExchangeSize}.";
            }
            else
            {
                var monto = item.UnitPrice * s.Quantity;
                var pago = (await _uow.StorePayments.ObtenerEntidadesAsync(p => p.OrderId == o.Id)).OrderByDescending(p => p.CreatedAt).FirstOrDefault();
                if (pago?.Method == "wallet")
                {
                    var wallet = await _uow.Wallets.ObtenerEntidadAsync(w => w.UserId == o.UserId);
                    if (wallet is null) { r.lpError("Billetera", "No se encontró la billetera del cliente."); return r; }
                    await _uow.EjecutarSqlAsync($"UPDATE wallets SET balance = balance + {monto}, updated_at = now() WHERE id = {wallet.Id}");
                    var despues = (await _uow.Wallets.ObtenerEntidadAsync(w => w.Id == wallet.Id))!.Balance;
                    var wtx = new WalletTransaction
                    {
                        Id = Guid.NewGuid(), WalletId = wallet.Id, TransactionType = "purchase_refund", Status = "completed",
                        Amount = monto, BalanceBefore = despues - monto, BalanceAfter = despues,
                        ReferenceType = "store_order", ReferenceId = o.Id, ReceiptNumber = ReceiptGenerator.Generate(), CreatedAt = DateTime.UtcNow,
                    };
                    _uow.WalletTransactions.Insertar(wtx);
                    await _uow.CompletarAsync();
                    await _uow.EjecutarSqlAsync($"UPDATE store_return_requests SET refund_wallet_transaction_id = {wtx.Id} WHERE id = {solicitudId}");
                    detalle = $"Devolución aprobada · {Dinero(monto)} reembolsados a la billetera" + (notaTexto is null ? "" : $" · {notaTexto}");
                    avisoTitulo = "Devolución aprobada";
                    avisoCuerpo = $"Te devolvimos {Dinero(monto)} a tu billetera.";
                }
                else
                {
                    detalle = $"Devolución aprobada · {Dinero(monto)} · reembolso en proceso" + (notaTexto is null ? "" : $" · {notaTexto}");
                    avisoTitulo = "Devolución aprobada";
                    avisoCuerpo = $"Te devolveremos {Dinero(monto)}.";
                }
            }

            Evento(o.Id, "change", null, detalle, actor);
            Aviso(o.UserId, avisoTitulo, avisoCuerpo, o.Id);
            await _uow.CompletarAsync();
            return await DtoAsync(o.Id, null);
        });
    }

    public async Task<Response<TStorePedido>> RechazarSolicitudAsync(Guid adminId, Guid solicitudId, string? nota)
    {
        var r = new Response<TStorePedido>();
        var s = await _uow.StoreReturnRequests.ObtenerEntidadAsync(x => x.Id == solicitudId);
        if (s is null) { r.lpError("Solicitud", "Solicitud no encontrada."); return r; }
        var o = await _uow.StoreOrders.ObtenerEntidadAsync(x => x.Id == s.OrderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        var actor = await ActorAsync(adminId, true);
        var notaTexto = NullSiVacio(nota, 300) ?? "No cumple con la política de cambios y devoluciones";

        return await EnTransaccionAsync(async () =>
        {
            var filas = await _uow.EjecutarSqlAsync(
                $@"UPDATE store_return_requests SET status = 'rejected', resolution_note = {notaTexto},
                       resolved_by_id = {adminId}, resolved_by_name = {actor.Name}, resolved_at = now(), updated_at = now()
                   WHERE id = {solicitudId} AND status = 'pending'");
            if (filas == 0) { r.lpError("Solicitud", "Esta solicitud ya fue resuelta."); return r; }
            Evento(o.Id, "change", null, $"Solicitud rechazada · {notaTexto}", actor);
            Aviso(o.UserId, "Solicitud rechazada", notaTexto, o.Id);
            await _uow.CompletarAsync();
            return await DtoAsync(o.Id, null);
        });
    }

    // ═══════════════════════════ Cliente o admin ═══════════════════════════

    public async Task<Response<TStorePedido>> CancelarAsync(Guid actorId, bool esAdmin, Guid orderId, string? motivo)
    {
        var o = await PedidoAccesibleAsync(actorId, esAdmin, orderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        var actor = await ActorAsync(actorId, esAdmin);
        var texto = NullSiVacio(motivo, 200) ?? (esAdmin ? "Cancelado por la tienda" : "Cancelado por el cliente");

        return await EnTransaccionAsync(async () =>
        {
            var res = await CancelarCoreAsync(o, actor, esAdmin, texto);
            return res.blnError ? res : await DtoAsync(orderId, esAdmin ? null : actorId);
        });
    }

    public async Task<Response<TStorePedido>> ReprogramarAsync(Guid actorId, bool esAdmin, Guid orderId, string ventana)
    {
        var r = new Response<TStorePedido>();
        var o = await PedidoAccesibleAsync(actorId, esAdmin, orderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        var v = (ventana ?? "").Trim();
        if (v.Length < 3 || v.Length > 60) { r.lpError("Entrega", "Elige una fecha de entrega válida."); return r; }
        var actor = await ActorAsync(actorId, esAdmin);

        return await EnTransaccionAsync(async () =>
        {
            // Con incidencia abierta se puede reprogramar en cualquier momento; sin incidencia, solo mientras
            // va en reparto (antes de eso no hay una ventana de entrega concreta que cambiar).
            var filas = await _uow.EjecutarSqlAsync(
                $@"UPDATE store_orders SET has_open_incident = false, reschedule_window = {v}, updated_at = now()
                   WHERE id = {orderId} AND is_cancelled = false AND (has_open_incident = true OR fulfillment_phase = 5)");
            if (filas == 0) { r.lpError("Pedido", "Este pedido no está en reparto todavía."); return r; }
            Evento(orderId, "reschedule", null, $"Entrega reprogramada · {v}", actor);
            if (esAdmin) Aviso(o.UserId, "Entrega reprogramada", v, orderId);
            await _uow.CompletarAsync();
            return await DtoAsync(orderId, esAdmin ? null : actorId);
        });
    }

    // ═══════════════════════════ Acciones del admin ═══════════════════════════

    public async Task<Response<TStorePedido>> ConfirmarPagoAsync(Guid adminId, Guid orderId)
    {
        var r = new Response<TStorePedido>();
        var o = await _uow.StoreOrders.ObtenerEntidadAsync(x => x.Id == orderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        var actor = await ActorAsync(adminId, true);

        return await EnTransaccionAsync(async () =>
        {
            var filas = await _uow.EjecutarSqlAsync(
                $@"UPDATE store_payments SET status = 'confirmed', verified_by_id = {adminId}, verified_by_name = {actor.Name}, verified_at = now(), updated_at = now()
                   WHERE order_id = {orderId} AND status = 'pending'
                     AND EXISTS (SELECT 1 FROM store_orders o WHERE o.id = store_payments.order_id AND o.is_cancelled = false)");
            if (filas == 0) { r.lpError("Pago", "Este pedido no tiene un pago pendiente de confirmar."); return r; }
            await _uow.EjecutarSqlAsync(
                $"UPDATE store_orders SET payment_status = 'confirmed', fulfillment_phase = GREATEST(fulfillment_phase, 1), updated_at = now() WHERE id = {orderId}");
            Evento(orderId, "payment", 1, "Pago confirmado", actor);
            Aviso(o.UserId, "Pago confirmado", $"Verificamos tu pago de {Dinero(o.Total)}.", orderId);
            await _uow.CompletarAsync();
            return await DtoAsync(orderId, null);
        });
    }

    public async Task<Response<TStorePedido>> RechazarPagoAsync(Guid adminId, Guid orderId)
    {
        var r = new Response<TStorePedido>();
        var o = await _uow.StoreOrders.ObtenerEntidadAsync(x => x.Id == orderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        var actor = await ActorAsync(adminId, true);
        const string motivo = "Pago no recibido";

        return await EnTransaccionAsync(async () =>
        {
            var filas = await _uow.EjecutarSqlAsync(
                $@"UPDATE store_payments SET status = 'rejected', rejected_reason = {motivo}, verified_by_id = {adminId}, verified_by_name = {actor.Name}, verified_at = now(), updated_at = now()
                   WHERE order_id = {orderId} AND status = 'pending'
                     AND EXISTS (SELECT 1 FROM store_orders o WHERE o.id = store_payments.order_id AND o.is_cancelled = false)");
            if (filas == 0) { r.lpError("Pago", "Este pedido no tiene un pago pendiente de revisar."); return r; }
            var res = await CancelarCoreAsync(o, actor, true, motivo);
            return res.blnError ? res : await DtoAsync(orderId, null);
        });
    }

    public Task<Response<TStorePedido>> IniciarPreparacionAsync(Guid adminId, Guid orderId) =>
        AvanzarAsync(adminId, orderId, 2, "Preparación iniciada",
            () => _uow.EjecutarSqlAsync($@"UPDATE store_orders SET fulfillment_phase = 2, updated_at = now()
                WHERE id = {orderId} AND fulfillment_phase = 1 AND is_cancelled = false AND has_open_incident = false"),
            "En preparación", _ => "Estamos preparando tu pedido.");

    public Task<Response<TStorePedido>> MarcarEmpacadoAsync(Guid adminId, Guid orderId, string paquete)
    {
        var p = (paquete ?? "").Trim();
        if (p.Length < 1 || p.Length > 60)
            return Task.FromResult(Error<TStorePedido>("Paquete", "Indica el paquete (por ejemplo: Caja M · 1,2 kg)."));
        return AvanzarAsync(adminId, orderId, 3, $"Empacado · {p}",
            () => _uow.EjecutarSqlAsync($@"UPDATE store_orders SET fulfillment_phase = 3, package_info = {p}, updated_at = now()
                WHERE id = {orderId} AND fulfillment_phase = 2 AND is_cancelled = false AND has_open_incident = false"),
            "Empacado", _ => "Tu pedido está listo para salir.");
    }

    public Task<Response<TStorePedido>> DespacharAsync(Guid adminId, Guid orderId)
    {
        var guia = $"CR-{RandomNumberGenerator.GetInt32(10000000, 100000000)}";
        return AvanzarAsync(adminId, orderId, 4, $"Guía {guia} · entregado a {Transportista}",
            () => _uow.EjecutarSqlAsync($@"UPDATE store_orders SET fulfillment_phase = 4, carrier_name = {Transportista}, tracking_number = {guia}, updated_at = now()
                WHERE id = {orderId} AND fulfillment_phase = 3 AND is_cancelled = false AND has_open_incident = false"),
            "Salió tu pedido", _ => $"{Transportista} · guía {guia}");
    }

    public Task<Response<TStorePedido>> IniciarRepartoAsync(Guid adminId, Guid orderId) =>
        AvanzarAsync(adminId, orderId, 5, "En reparto",
            () => _uow.EjecutarSqlAsync($@"UPDATE store_orders SET fulfillment_phase = 5, updated_at = now()
                WHERE id = {orderId} AND fulfillment_phase = 4 AND is_cancelled = false AND has_open_incident = false"),
            "Tu pedido va en camino", o => $"Código de entrega: {o.DeliveryCode}");

    public async Task<Response<TStorePedido>> RegistrarIncidenciaAsync(Guid adminId, Guid orderId, string motivo)
    {
        var r = new Response<TStorePedido>();
        var m = (motivo ?? "").Trim();
        if (m.Length < 3 || m.Length > 200) { r.lpError("Incidencia", "Describe el motivo del intento fallido."); return r; }
        var o = await _uow.StoreOrders.ObtenerEntidadAsync(x => x.Id == orderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        var max = Int(await CargarAjustesAsync(), "max_delivery_attempts", 3);
        if (o.IncidentCount >= max)
        { r.lpError("Pedido", $"Ya se registraron {max} intentos fallidos. Cancela el pedido o contacta al cliente directamente."); return r; }
        var actor = await ActorAsync(adminId, true);

        return await EnTransaccionAsync(async () =>
        {
            var filas = await _uow.EjecutarSqlAsync(
                $@"UPDATE store_orders SET has_open_incident = true, incident_count = incident_count + 1, updated_at = now()
                   WHERE id = {orderId} AND fulfillment_phase = 5 AND is_cancelled = false AND has_open_incident = false AND incident_count < {max}");
            if (filas == 0) { r.lpError("Pedido", "Solo se puede registrar una incidencia en un pedido en reparto."); return r; }
            Evento(orderId, "incident", null, $"Intento fallido · {m}", actor);
            Aviso(o.UserId, "No pudimos entregar", $"{m}. Elige una nueva fecha de entrega.", orderId);
            await _uow.CompletarAsync();
            return await DtoAsync(orderId, null);
        });
    }

    public async Task<Response<TStorePedido>> ConfirmarEntregaAsync(Guid adminId, Guid orderId, string codigo)
    {
        var r = new Response<TStorePedido>();
        var o = await _uow.StoreOrders.ObtenerEntidadAsync(x => x.Id == orderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        if (o.IsCancelled || o.HasOpenIncident || o.FulfillmentPhase != 5)
        { r.lpError("Pedido", "El pedido no está listo para confirmar la entrega."); return r; }
        if (o.CodeLockedUntil is { } bloqueo && bloqueo > DateTime.UtcNow)
        {
            var min = (int)Math.Ceiling((bloqueo - DateTime.UtcNow).TotalMinutes);
            r.lpError("Código bloqueado", $"Demasiados intentos. Intenta de nuevo en {min} min."); return r;
        }

        var dado = Encoding.UTF8.GetBytes((codigo ?? "").Trim());
        var real = Encoding.UTF8.GetBytes(o.DeliveryCode);
        if (!CryptographicOperations.FixedTimeEquals(dado, real))
        {
            // El intento fallido se guarda siempre (fuera de la transacción de éxito).
            var max = Int(await CargarAjustesAsync(), "code_max_attempts", 5);
            await _uow.EjecutarSqlAsync(
                $@"UPDATE store_orders SET
                     code_attempts = CASE WHEN code_attempts + 1 >= {max} THEN 0 ELSE code_attempts + 1 END,
                     code_locked_until = CASE WHEN code_attempts + 1 >= {max} THEN now() + interval '30 minutes' ELSE code_locked_until END,
                     updated_at = now()
                   WHERE id = {orderId}");
            r.lpError("Código incorrecto", "El código de entrega no coincide.");
            return r;
        }

        return await AvanzarAsync(adminId, orderId, 6, "Entregado · código verificado",
            () => _uow.EjecutarSqlAsync($@"UPDATE store_orders SET fulfillment_phase = 6, code_attempts = 0, code_locked_until = NULL, updated_at = now()
                WHERE id = {orderId} AND fulfillment_phase = 5 AND is_cancelled = false AND has_open_incident = false"),
            "Entregado", _ => "Gracias por tu compra.");
    }

    // ═══════════════════════════ Productos (admin) ═══════════════════════════

    public async Task<Response<TStoreProducto>> CrearProductoAsync(TStoreGuardarProducto d)
    {
        var (error, categoria, nombre, descripcion) = await ValidarProductoAsync(d);
        if (error is not null) return error;
        var stock = d.Stock ?? 0;
        if (stock < 0 || stock > 100000) return Error<TStoreProducto>("Producto", "El stock debe ser un número entero desde 0.");

        var ahora = DateTime.UtcNow;
        var sku = $"BR-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var producto = new StoreProduct
        {
            Id = Guid.NewGuid(), Sku = sku, Name = nombre, CategoryId = categoria!.Id,
            Description = descripcion ?? "Producto oficial de Biozin Royale.",
            Price = d.Price, ArtType = "pin", ArtVariant = "", Badge = "", RequiresSize = false,
            IsVisible = d.IsVisible ?? true, CreatedAt = ahora, UpdatedAt = ahora,
        };
        await using var tx = await _uow.IniciarTransaccionAsync();
        _uow.StoreProducts.Insertar(producto);
        await _uow.CompletarAsync();
        _uow.StoreProductVariants.Insertar(new StoreProductVariant
        {
            Id = Guid.NewGuid(), ProductId = producto.Id, Size = null, Sku = sku, Stock = stock, CreatedAt = ahora, UpdatedAt = ahora,
        });
        await _uow.CompletarAsync();
        await tx.CommitAsync();
        return await ProductoDtoAsync(producto.Id);
    }

    public async Task<Response<TStoreProducto>> ActualizarProductoAsync(Guid id, TStoreGuardarProducto d)
    {
        var p = await _uow.StoreProducts.ObtenerEntidadAsync(x => x.Id == id && x.DeletedAt == null);
        if (p is null) return Error<TStoreProducto>("Producto", "Producto no encontrado.");
        var (error, categoria, nombre, descripcion) = await ValidarProductoAsync(d);
        if (error is not null) return error;
        if (d.Stock is < 0 or > 100000) return Error<TStoreProducto>("Producto", "El stock debe ser un número entero desde 0.");

        await using var tx = await _uow.IniciarTransaccionAsync();
        p.Name = nombre!; p.CategoryId = categoria!.Id; p.Price = d.Price;
        p.Description = descripcion ?? p.Description; p.IsVisible = d.IsVisible ?? p.IsVisible; p.UpdatedAt = DateTime.UtcNow;
        _uow.StoreProducts.Modificar(p);
        await _uow.CompletarAsync();
        // El stock total solo se fija en productos sin talla; con tallas se ajusta talla por talla.
        if (d.Stock is { } s && !p.RequiresSize)
            await _uow.EjecutarSqlAsync($"UPDATE store_product_variants SET stock = {s}, updated_at = now() WHERE product_id = {id} AND size IS NULL");
        await tx.CommitAsync();
        return await ProductoDtoAsync(id);
    }

    public async Task<Response<bool>> EliminarProductoAsync(Guid id)
    {
        // Borrado lógico: los pedidos anteriores conservan su historial.
        var filas = await _uow.EjecutarSqlAsync(
            $"UPDATE store_products SET deleted_at = now(), is_visible = false, updated_at = now() WHERE id = {id} AND deleted_at IS NULL");
        return filas == 0
            ? Error<bool>("Producto", "Producto no encontrado.")
            : new Response<bool> { ReturnValue = true };
    }

    public async Task<Response<TStoreProducto>> AjustarStockAsync(Guid id, TStoreAjusteStock d)
    {
        if (d.Delta == 0 || Math.Abs(d.Delta) > 100000) return Error<TStoreProducto>("Stock", "Ajuste inválido.");
        var talla = string.IsNullOrWhiteSpace(d.Size) ? null : d.Size.Trim().ToUpperInvariant();
        var filas = await _uow.EjecutarSqlAsync(
            $@"UPDATE store_product_variants v SET stock = v.stock + {d.Delta}, updated_at = now()
               FROM store_products p
               WHERE v.product_id = p.id AND p.id = {id} AND p.deleted_at IS NULL AND v.is_active
                 AND upper(coalesce(v.size, '')) = {talla ?? ""} AND v.stock + {d.Delta} >= 0");
        if (filas == 0) return Error<TStoreProducto>("Stock", "No se pudo ajustar: revisa la talla y que el stock no quede negativo.");
        return await ProductoDtoAsync(id);
    }

    private async Task<(Response<TStoreProducto>? Error, StoreCategory? Categoria, string? Nombre, string? Descripcion)> ValidarProductoAsync(TStoreGuardarProducto d)
    {
        var nombre = (d.Name ?? "").Trim();
        if (nombre.Length < 2 || nombre.Length > 120) return (Error<TStoreProducto>("Producto", "Escribe el nombre del producto."), null, null, null);
        if (d.Price <= 0 || d.Price > 100000) return (Error<TStoreProducto>("Producto", "El precio debe ser un número mayor que 0."), null, null, null);
        var descripcion = NullSiVacio(d.Description, 500);
        var categoria = (await _uow.StoreCategories.ObtenerEntidadesAsync(c => c.IsActive))
            .FirstOrDefault(c => string.Equals(c.Name, (d.Category ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        if (categoria is null) return (Error<TStoreProducto>("Producto", "Elige una categoría válida."), null, null, null);
        return (null, categoria, nombre, descripcion);
    }

    private async Task<Response<TStoreProducto>> ProductoDtoAsync(Guid id)
    {
        var lista = (await ListarProductosAsync(true)).ReturnValue;
        var p = lista.FirstOrDefault(x => x.Id == id);
        return p is null ? Error<TStoreProducto>("Producto", "Producto no encontrado.") : new Response<TStoreProducto> { ReturnValue = p };
    }

    // ═══════════════════════════ Avisos del cliente ═══════════════════════════

    public async Task<Response<List<TStoreNotificacion>>> ListarNotificacionesAsync(Guid userId)
    {
        var lista = (await _uow.StoreNotifications.ObtenerEntidadesAsync(n => n.UserId == userId))
            .OrderByDescending(n => n.CreatedAt).Take(100)
            .Select(n => new TStoreNotificacion
            {
                Id = n.Id, Title = n.Title, Body = n.Body, OrderId = n.ReferenceId,
                Read = n.ReadAt != null, CreatedAt = n.CreatedAt,
            }).ToList();
        return new Response<List<TStoreNotificacion>> { ReturnValue = lista };
    }

    public async Task<Response<bool>> MarcarLeidaAsync(Guid userId, Guid id)
    {
        await _uow.EjecutarSqlAsync($"UPDATE store_notifications SET read_at = now() WHERE id = {id} AND user_id = {userId} AND read_at IS NULL");
        return new Response<bool> { ReturnValue = true };
    }

    public async Task<Response<bool>> MarcarTodasLeidasAsync(Guid userId)
    {
        await _uow.EjecutarSqlAsync($"UPDATE store_notifications SET read_at = now() WHERE user_id = {userId} AND read_at IS NULL");
        return new Response<bool> { ReturnValue = true };
    }

    // ═══════════════════════════ Núcleo de cancelación ═══════════════════════════

    /// Cancela, devuelve el stock y reembolsa si el pago ya estaba confirmado. Debe correr dentro de
    /// una transacción; si devuelve error, quien llama revierte todo.
    private async Task<Response<TStorePedido>> CancelarCoreAsync(StoreOrder o, TStoreActor actor, bool esAdmin, string motivo)
    {
        var r = new Response<TStorePedido>();
        var maxFase = (short)(esAdmin ? 3 : 2);   // el cliente hasta "en preparación"; el admin hasta "empacado"

        var filas = await _uow.EjecutarSqlAsync(
            $@"UPDATE store_orders SET is_cancelled = true, cancelled_at = now(), cancelled_by_id = {actor.Id!.Value},
                   cancelled_by_role = {actor.Role}, cancel_reason = {motivo}, updated_at = now()
               WHERE id = {o.Id} AND is_cancelled = false AND fulfillment_phase <= {maxFase}");
        if (filas == 0) { r.lpError("Pedido", "Este pedido ya no se puede cancelar."); return r; }

        // Stock de vuelta
        var items = await _uow.StoreOrderItems.ObtenerEntidadesAsync(i => i.OrderId == o.Id);
        foreach (var i in items)
            await _uow.EjecutarSqlAsync($"UPDATE store_product_variants SET stock = stock + {i.Quantity}, updated_at = now() WHERE id = {i.VariantId}");

        // Pago
        var pago = (await _uow.StorePayments.ObtenerEntidadesAsync(p => p.OrderId == o.Id))
            .OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        var estadoPedido = pago?.Status ?? "pending";
        var detalle = motivo;
        var aviso = motivo;

        if (pago is { Status: "confirmed" })
        {
            var marcado = await _uow.EjecutarSqlAsync(
                $"UPDATE store_payments SET status = 'refunded', refunded_at = now(), updated_at = now() WHERE id = {pago.Id} AND status = 'confirmed'");
            if (marcado == 0) { r.lpError("Pago", "No se pudo reembolsar el pago. Intenta de nuevo."); return r; }
            estadoPedido = "refunded";

            if (pago.Method == "wallet")
            {
                var wallet = await _uow.Wallets.ObtenerEntidadAsync(w => w.UserId == o.UserId);
                if (wallet is null) { r.lpError("Billetera", "No se encontró la billetera del cliente."); return r; }
                await _uow.EjecutarSqlAsync($"UPDATE wallets SET balance = balance + {pago.Amount}, updated_at = now() WHERE id = {wallet.Id}");
                var despues = (await _uow.Wallets.ObtenerEntidadAsync(w => w.Id == wallet.Id))!.Balance;
                var wtx = new WalletTransaction
                {
                    Id = Guid.NewGuid(), WalletId = wallet.Id, TransactionType = "purchase_refund", Status = "completed",
                    Amount = pago.Amount, BalanceBefore = despues - pago.Amount, BalanceAfter = despues,
                    ReferenceType = "store_order", ReferenceId = o.Id,
                    ReceiptNumber = ReceiptGenerator.Generate(), CreatedAt = DateTime.UtcNow,
                };
                _uow.WalletTransactions.Insertar(wtx);
                await _uow.CompletarAsync();
                await _uow.EjecutarSqlAsync($"UPDATE store_payments SET refund_wallet_transaction_id = {wtx.Id} WHERE id = {pago.Id}");
                detalle = $"{motivo} · reembolsado a la billetera";
                aviso = "Te devolvimos tu pago a la billetera.";
            }
            else
            {
                detalle = $"{motivo} · reembolso en proceso";
                aviso = "Te devolveremos tu pago.";
            }
        }
        else if (pago is { Status: "pending" })
        {
            await _uow.EjecutarSqlAsync(
                $"UPDATE store_payments SET status = 'rejected', rejected_reason = {motivo}, updated_at = now() WHERE id = {pago.Id} AND status = 'pending'");
            estadoPedido = "rejected";
        }

        await _uow.EjecutarSqlAsync($"UPDATE store_orders SET payment_status = {estadoPedido} WHERE id = {o.Id}");
        Evento(o.Id, "cancellation", null, detalle, actor);
        Aviso(o.UserId, "Pedido cancelado", aviso, o.Id);
        await _uow.CompletarAsync();
        return r;
    }

    // ═══════════════════════════ Helpers ═══════════════════════════

    private async Task<Response<TStorePedido>> AvanzarAsync(
        Guid adminId, Guid orderId, int hasta, string mensaje, Func<Task<int>> actualizar,
        string avisoTitulo, Func<StoreOrder, string> avisoCuerpo)
    {
        var r = new Response<TStorePedido>();
        var o = await _uow.StoreOrders.ObtenerEntidadAsync(x => x.Id == orderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        var actor = await ActorAsync(adminId, true);

        return await EnTransaccionAsync(async () =>
        {
            if (await actualizar() == 0) { r.lpError("Pedido", "El pedido no está en la etapa correcta para esta acción."); return r; }
            Evento(orderId, "phase", hasta, mensaje, actor);
            Aviso(o.UserId, avisoTitulo, avisoCuerpo(o), orderId);
            await _uow.CompletarAsync();
            return await DtoAsync(orderId, null);
        });
    }

    private async Task<Response<T>> EnTransaccionAsync<T>(Func<Task<Response<T>>> operacion)
    {
        await using var tx = await _uow.IniciarTransaccionAsync();
        var res = await operacion();
        if (res.blnError) await tx.RollbackAsync();
        else await tx.CommitAsync();
        return res;
    }

    private async Task<StoreOrder?> PedidoAccesibleAsync(Guid actorId, bool esAdmin, Guid orderId)
    {
        var o = await _uow.StoreOrders.ObtenerEntidadAsync(x => x.Id == orderId);
        return o is null || (!esAdmin && o.UserId != actorId) ? null : o;
    }

    private async Task<TStoreActor> ActorAsync(Guid id, bool admin)
    {
        if (admin)
        {
            var s = await _uow.StaffMembers.ObtenerEntidadAsync(x => x.Id == id);
            return new TStoreActor(id, "admin", Trunc(s?.DisplayName ?? "Admin", 120));
        }
        var p = await _uow.Profiles.ObtenerEntidadAsync(x => x.UserId == id);
        return new TStoreActor(id, "user", Trunc(p?.DisplayName ?? p?.Username ?? "Cliente", 120));
    }

    private void Evento(Guid orderId, string tipo, int? fase, string mensaje, TStoreActor a) =>
        _uow.StoreOrderEvents.Insertar(new StoreOrderEvent
        {
            Id = Guid.NewGuid(), OrderId = orderId, EventType = tipo, Phase = (short?)fase, Message = mensaje,
            ActorId = a.Id, ActorRole = a.Role, ActorName = a.Name, CreatedAt = DateTime.UtcNow,
        });

    private void Aviso(Guid userId, string titulo, string cuerpo, Guid orderId) =>
        _uow.StoreNotifications.Insertar(new StoreNotification
        {
            Id = Guid.NewGuid(), UserId = userId, NotificationType = "order", Title = titulo, Body = cuerpo,
            ReferenceType = "store_order", ReferenceId = orderId, CreatedAt = DateTime.UtcNow,
        });

    private async Task<Response<TStorePedido>> DtoAsync(Guid orderId, Guid? visor)
    {
        var o = await _uow.StoreOrders.ObtenerEntidadAsync(x => x.Id == orderId);
        if (o is null) return NoEncontrado<TStorePedido>();
        return new Response<TStorePedido> { ReturnValue = (await ConstruirAsync([o], visor))[0] };
    }

    /// visor = id del cliente que consulta; null cuando consulta el admin (no recibe el código de entrega).
    private async Task<List<TStorePedido>> ConstruirAsync(List<StoreOrder> ordenes, Guid? visor)
    {
        if (ordenes.Count == 0) return [];
        var ids = ordenes.Select(o => o.Id).ToList();
        var userIds = ordenes.Select(o => o.UserId).Distinct().ToList();

        var items = await _uow.StoreOrderItems.ObtenerEntidadesAsync(i => ids.Contains(i.OrderId));
        var pagos = await _uow.StorePayments.ObtenerEntidadesAsync(p => ids.Contains(p.OrderId));
        var eventos = await _uow.StoreOrderEvents.ObtenerEntidadesAsync(e => ids.Contains(e.OrderId));
        var devoluciones = await _uow.StoreReturnRequests.ObtenerEntidadesAsync(s => ids.Contains(s.OrderId));
        var perfiles = (await _uow.Profiles.ObtenerEntidadesAsync(p => userIds.Contains(p.UserId)))
            .ToDictionary(p => p.UserId, p => p);
        var itemsPorId = items.ToDictionary(i => i.Id);

        return ordenes.Select(o =>
        {
            var pago = pagos.Where(p => p.OrderId == o.Id).OrderByDescending(p => p.CreatedAt).FirstOrDefault();
            perfiles.TryGetValue(o.UserId, out var perfil);
            var esDueno = visor == o.UserId;
            return new TStorePedido
            {
                Id = o.Id, OrderNumber = o.OrderNumber, UserId = o.UserId,
                CustomerName = o.CustomerName, Email = perfil?.Email ?? "", Phone = o.Phone,
                Province = o.Province, Canton = o.Canton, District = o.District, AddressLine = o.AddressLine,
                Subtotal = o.Subtotal, ShippingCost = o.ShippingCost, Total = o.Total,
                PaymentMethod = pago?.Method ?? "", PaymentStatus = o.PaymentStatus,
                CardLastFour = pago?.CardLastFour, CardNetwork = pago?.CardNetwork,
                FulfillmentPhase = o.FulfillmentPhase, IsCancelled = o.IsCancelled, CancelReason = o.CancelReason,
                CarrierName = o.CarrierName, TrackingNumber = o.TrackingNumber, PackageInfo = o.PackageInfo,
                DeliveryCode = esDueno && !o.IsCancelled && o.FulfillmentPhase >= 5 ? o.DeliveryCode : null,
                HasOpenIncident = o.HasOpenIncident, IncidentCount = o.IncidentCount, RescheduleWindow = o.RescheduleWindow,
                EtaFrom = o.EtaFrom, EtaTo = o.EtaTo, Rating = o.Rating, CreatedAt = o.CreatedAt,
                Items = items.Where(i => i.OrderId == o.Id).OrderBy(i => i.CreatedAt).ThenBy(i => i.ProductName)
                    .Select(i => new TStoreItemPedido { Id = i.Id, ProductId = i.ProductId, Name = i.ProductName, Size = i.Size, Quantity = i.Quantity, UnitPrice = i.UnitPrice }).ToList(),
                Events = eventos.Where(e => e.OrderId == o.Id).OrderBy(e => e.CreatedAt)
                    .Select(e => new TStoreEvento { Id = e.Id, Type = e.EventType, Phase = e.Phase, Message = e.Message, ActorName = e.ActorName, ActorRole = e.ActorRole, CreatedAt = e.CreatedAt }).ToList(),
                Returns = devoluciones.Where(s => s.OrderId == o.Id).OrderBy(s => s.CreatedAt)
                    .Select(s => new TStoreSolicitud
                    {
                        Id = s.Id, OrderItemId = s.OrderItemId,
                        ProductName = itemsPorId.TryGetValue(s.OrderItemId, out var it) ? it.ProductName : "",
                        Size = itemsPorId.TryGetValue(s.OrderItemId, out var it2) ? it2.Size : null,
                        Quantity = s.Quantity, RequestType = s.RequestType, Reason = s.Reason, Detail = s.Detail,
                        ExchangeSize = s.ExchangeSize, Status = s.Status, ResolutionNote = s.ResolutionNote,
                        ResolvedByName = s.ResolvedByName, ResolvedAt = s.ResolvedAt, CreatedAt = s.CreatedAt,
                    }).ToList(),
            };
        }).ToList();
    }

    private async Task<Dictionary<string, string>> CargarAjustesAsync() =>
        (await _uow.StoreSettings.ListarAsync()).ToDictionary(s => s.SettingKey, s => s.SettingValue);

    private static decimal Dec(Dictionary<string, string> d, string k, decimal def) =>
        d.TryGetValue(k, out var v) && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var r) ? r : def;
    private static int Int(Dictionary<string, string> d, string k, int def) =>
        d.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : def;
    private static bool Bool(Dictionary<string, string> d, string k, bool def) =>
        d.TryGetValue(k, out var v) && bool.TryParse(v, out var r) ? r : def;

    private static Response<T> Error<T>(string titulo, string detalle) { var r = new Response<T>(); r.lpError(titulo, detalle); return r; }
    private static Response<T> NoEncontrado<T>() => Error<T>("Pedido", "Pedido no encontrado.");

    private static string Codigo(StoreOrder o) => $"BR-{o.OrderNumber}";
    private static string Dinero(decimal n) => $"${n.ToString("F2", CultureInfo.InvariantCulture)}";
    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
    private static string? NullSiVacio(string? s, int max)
    {
        var t = (s ?? "").Trim();
        return t.Length == 0 ? null : Trunc(t, max);
    }
    private static int TallaOrden(string? s) => s switch { "XS" => 0, "S" => 1, "M" => 2, "L" => 3, "XL" => 4, "XXL" => 5, _ => 9 };

    private static DateTime HoyCostaRica()
    {
        try { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica")).Date; }
        catch { return DateTime.UtcNow.AddHours(-6).Date; }
    }
    private static DateTime SumarHabiles(DateTime desde, int n)
    {
        var d = desde.Date;
        for (var k = 0; k < n;)
        {
            d = d.AddDays(1);
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) k++;
        }
        return d;
    }
}
