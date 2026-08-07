using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Stripe;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;
using Microsoft.Extensions.Configuration;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class DepositosLN : IDepositosLN
{
    private readonly IUnitWork          _unitOfWork;
    private readonly IConfiguration     _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IEmailService      _emailService;

    public DepositosLN(IUnitWork unitOfWork, IConfiguration config, IHttpClientFactory httpFactory, IEmailService emailService)
    {
        _unitOfWork   = unitOfWork;
        _config       = config;
        _httpFactory  = httpFactory;
        _emailService = emailService;
        StripeConfiguration.ApiKey = _config["Payments:Stripe:SecretKey"]!;
    }

    // ── Stripe ────────────────────────────────────────────────────────────────

    public async Task<Response<TIniciarDepositoResultado>> IniciarStripeAsync(Guid userId, decimal amount)
    {
        var resultado = new Response<TIniciarDepositoResultado>();

        if (amount < 1)
        {
            resultado.lpError("Monto inválido", "El monto mínimo de depósito es $1.");
            return resultado;
        }

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId).ReturnValue;
        if (wallet is null)
        {
            resultado.lpError("Billetera", "No se encontró la billetera del usuario.");
            return resultado;
        }

        var options = new PaymentIntentCreateOptions
        {
            Amount             = (long)(amount * 100),
            Currency           = "usd",
            PaymentMethodTypes = new List<string> { "card" },
            Metadata           = new Dictionary<string, string>
            {
                { "walletId", wallet.Id.ToString() },
                { "userId",   userId.ToString()    },
            },
        };

        var service = new PaymentIntentService();
        var intent  = await service.CreateAsync(options);

        var tx = new WalletTransaction
        {
            Id              = Guid.NewGuid(),
            WalletId        = wallet.Id,
            TransactionType = "deposit",
            Status          = "pending",
            Amount          = amount,
            BalanceBefore   = wallet.Balance,
            BalanceAfter    = wallet.Balance + amount,
            ReferenceType   = "stripe",
            Metadata        = intent.Id,
            ReceiptNumber   = ReceiptGenerator.Generate(),
            CreatedAt       = DateTime.UtcNow,
        };
        _unitOfWork.WalletTransactions.Insertar(tx);
        _unitOfWork.Completar();

        resultado.ReturnValue = new TIniciarDepositoResultado
        {
            TransactionId = tx.Id,
            Provider      = "stripe",
            ClientSecret  = intent.ClientSecret,
            BalanceBefore = wallet.Balance,
            ReceiptNumber = tx.ReceiptNumber,
        };
        return resultado;
    }

    public async Task<Response<bool>> ProcesarWebhookStripeAsync(string payload, string signature)
    {
        var resultado = new Response<bool>();
        try
        {
            var secret      = _config["Payments:Stripe:WebhookSecret"]!;
            var stripeEvent = EventUtility.ConstructEvent(payload, signature, secret,
                throwOnApiVersionMismatch: false);

            if (stripeEvent.Type == EventTypes.PaymentIntentSucceeded)
            {
                var intent = (PaymentIntent)stripeEvent.Data.Object;
                await ConfirmarDepositoInternoAsync(intent.Id, "stripe");
            }
        }
        catch (StripeException)
        {
            resultado.lpError("Webhook", "Firma Stripe inválida.");
            return resultado;
        }

        resultado.ReturnValue = true;
        return resultado;
    }

    // ── PayPal ────────────────────────────────────────────────────────────────

    public async Task<Response<TIniciarDepositoResultado>> IniciarPayPalAsync(Guid userId, decimal amount)
    {
        var resultado = new Response<TIniciarDepositoResultado>();

        if (amount < 1)
        {
            resultado.lpError("Monto inválido", "El monto mínimo de depósito es $1.");
            return resultado;
        }

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId).ReturnValue;
        if (wallet is null)
        {
            resultado.lpError("Billetera", "No se encontró la billetera del usuario.");
            return resultado;
        }

        string token;
        try   { token = await ObtenerTokenPayPalAsync(); }
        catch { resultado.lpError("PayPal", "Error al conectar con PayPal."); return resultado; }

        string orderId;
        try   { orderId = await CrearOrdenPayPalAsync(amount, token); }
        catch { resultado.lpError("PayPal", "Error al crear la orden de pago."); return resultado; }

        var tx = new WalletTransaction
        {
            Id              = Guid.NewGuid(),
            WalletId        = wallet.Id,
            TransactionType = "deposit",
            Status          = "pending",
            Amount          = amount,
            BalanceBefore   = wallet.Balance,
            BalanceAfter    = wallet.Balance + amount,
            ReferenceType   = "paypal",
            Metadata        = orderId,
            ReceiptNumber   = ReceiptGenerator.Generate(),
            CreatedAt       = DateTime.UtcNow,
        };
        _unitOfWork.WalletTransactions.Insertar(tx);
        _unitOfWork.Completar();

        resultado.ReturnValue = new TIniciarDepositoResultado
        {
            TransactionId = tx.Id,
            Provider      = "paypal",
            OrderId       = orderId,
            BalanceBefore = wallet.Balance,
            ReceiptNumber = tx.ReceiptNumber,
        };
        return resultado;
    }

    public async Task<Response<decimal>> CapturarPayPalAsync(Guid userId, string orderId)
    {
        var resultado = new Response<decimal>();

        string token;
        try   { token = await ObtenerTokenPayPalAsync(); }
        catch { resultado.lpError("PayPal", "Error al conectar con PayPal."); return resultado; }

        bool capturado;
        try   { capturado = await EjecutarCapturaPayPalAsync(orderId, token); }
        catch { resultado.lpError("PayPal", "Error al capturar el pago."); return resultado; }

        if (!capturado)
        {
            resultado.lpError("PayPal", "El pago no pudo ser confirmado por PayPal.");
            return resultado;
        }

        var nuevoBalance = await ConfirmarDepositoInternoAsync(orderId, "paypal");
        if (nuevoBalance is null)
        {
            resultado.lpError("PayPal", "Pago capturado pero no se encontró la transacción pendiente.");
            return resultado;
        }
        resultado.ReturnValue = nuevoBalance.Value;
        return resultado;
    }

    // ── Interno ───────────────────────────────────────────────────────────────

    private async Task<decimal?> ConfirmarDepositoInternoAsync(string externalId, string provider)
    {
        var tx = _unitOfWork.WalletTransactions
            .ObtenerEntidad(t => t.Metadata == externalId
                && t.ReferenceType == provider
                && t.Status == "pending")
            .ReturnValue;

        if (tx is null) return null;

        var balanceAntes = tx.BalanceBefore;
        tx.Status = "completed";
        _unitOfWork.WalletTransactions.Modificar(tx);

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.Id == tx.WalletId).ReturnValue!;
        wallet.Balance   = Math.Round(wallet.Balance + tx.Amount, 2);
        wallet.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Wallets.Modificar(wallet);
        _unitOfWork.Completar();

        // Enviar comprobante por email (fire-and-forget; no bloquea la respuesta)
        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == wallet.UserId).ReturnValue;
        if (perfil is not null && !string.IsNullOrEmpty(perfil.Email))
        {
            var remitente = _config["Mail:Remitente"] ?? _config["Mail:Usuario"]!;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.EnviarComprobanteTransaccionAsync(
                        correoDestino:   perfil.Email,
                        nombre:          perfil.DisplayName ?? perfil.Username,
                        receiptNumber:   tx.ReceiptNumber ?? "–",
                        tipo:            "deposit",
                        monto:           tx.Amount,
                        balanceAntes:    balanceAntes,
                        balanceDespues:  wallet.Balance,
                        estado:          "completed",
                        fecha:           tx.CreatedAt,
                        correoRemitente: remitente);
                }
                catch { /* no propagar: el depósito ya fue procesado */ }
            });
        }

        return wallet.Balance;
    }

    // ── PayPal HTTP ───────────────────────────────────────────────────────────

    private async Task<string> ObtenerTokenPayPalAsync()
    {
        var clientId     = _config["Payments:PayPal:ClientId"]!;
        var clientSecret = _config["Payments:PayPal:ClientSecret"]!;
        var baseUrl      = _config["Payments:PayPal:BaseUrl"]!;

        var http        = _httpFactory.CreateClient();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var body     = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });
        var response = await http.PostAsync($"{baseUrl}/v1/oauth2/token", body);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc  = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<string> CrearOrdenPayPalAsync(decimal amount, string token)
    {
        var baseUrl = _config["Payments:PayPal:BaseUrl"]!;
        var http    = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var body = new
        {
            intent         = "CAPTURE",
            purchase_units = new[]
            {
                new { amount = new { currency_code = "USD", value = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) } }
            },
        };

        var content = new StringContent(JsonSerializer.Serialize(body));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await http.PostAsync($"{baseUrl}/v2/checkout/orders", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc  = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private async Task<bool> EjecutarCapturaPayPalAsync(string orderId, string token)
    {
        var baseUrl = _config["Payments:PayPal:BaseUrl"]!;
        var http    = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var content  = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await http.PostAsync($"{baseUrl}/v2/checkout/orders/{orderId}/capture", content);
        response.EnsureSuccessStatusCode();

        var json   = await response.Content.ReadAsStringAsync();
        var doc    = JsonDocument.Parse(json);
        var status = doc.RootElement.GetProperty("status").GetString();
        return status == "COMPLETED";
    }
}
