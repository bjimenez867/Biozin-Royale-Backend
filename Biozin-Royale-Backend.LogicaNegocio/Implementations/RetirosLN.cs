using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;
using Microsoft.Extensions.Configuration;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class RetirosLN : IRetirosLN
{
    private readonly IUnitWork          _unitOfWork;
    private readonly IConfiguration     _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IEmailService      _emailService;

    public RetirosLN(IUnitWork unitOfWork, IConfiguration config, IHttpClientFactory httpFactory, IEmailService emailService)
    {
        _unitOfWork   = unitOfWork;
        _config       = config;
        _httpFactory  = httpFactory;
        _emailService = emailService;
    }

    public async Task<Response<TRetiroResultado>> ProcesarRetiroAsync(Guid userId, TIniciarRetiroRequest request)
    {
        var resultado = new Response<TRetiroResultado>();

        if (request.Amount < 5)
        {
            resultado.lpError("Monto inválido", "El monto mínimo de retiro es $5.");
            return resultado;
        }

        var wallet = _unitOfWork.Wallets.ObtenerEntidad(w => w.UserId == userId).ReturnValue;
        if (wallet is null)
        {
            resultado.lpError("Billetera", "No se encontró la billetera del usuario.");
            return resultado;
        }

        if (wallet.Balance < request.Amount)
        {
            resultado.lpError("Saldo insuficiente", "No tenés suficiente saldo para realizar este retiro.");
            return resultado;
        }

        var metodo = _unitOfWork.PaymentMethods
            .ObtenerEntidad(m => m.Id == request.PaymentMethodId && m.WalletId == wallet.Id)
            .ReturnValue;

        if (metodo is null)
        {
            resultado.lpError("Método de pago", "No se encontró el método de pago seleccionado.");
            return resultado;
        }

        if (metodo.Type == "paypal")
            return await ProcesarPayPalAsync(wallet, metodo, request.Amount);

        // Tarjeta: retiro pendiente, se procesa manualmente
        return ProcesarTarjetaPendiente(wallet, metodo, request.Amount);
    }

    // ── PayPal (automático) ───────────────────────────────────────────────────

    private async Task<Response<TRetiroResultado>> ProcesarPayPalAsync(
        Wallet wallet, PaymentMethod metodo, decimal amount)
    {
        var resultado = new Response<TRetiroResultado>();

        var tx = CrearTransaccion(wallet, "paypal", metodo.Email!, amount);
        _unitOfWork.WalletTransactions.Insertar(tx);
        _unitOfWork.Completar();  // persiste la tx como "pending" antes de llamar PayPal

        string token;
        try   { token = await ObtenerTokenPayPalAsync(); }
        catch
        {
            tx.Status = "failed";
            _unitOfWork.WalletTransactions.Modificar(tx);
            _unitOfWork.Completar();
            resultado.lpError("PayPal", "Error al conectar con PayPal. Intentá de nuevo.");
            return resultado;
        }

        string batchId;
        try   { batchId = await EnviarPayoutPayPalAsync(amount, metodo.Email!, tx.Id.ToString(), token); }
        catch
        {
            tx.Status = "failed";
            _unitOfWork.WalletTransactions.Modificar(tx);
            _unitOfWork.Completar();
            resultado.lpError("PayPal", "No se pudo procesar el pago con PayPal. Verificá que la cuenta esté activa.");
            return resultado;
        }

        tx.Status   = "completed";
        tx.Metadata = batchId;
        _unitOfWork.WalletTransactions.Modificar(tx);

        wallet.Balance   = Math.Round(wallet.Balance - amount, 2);
        wallet.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Wallets.Modificar(wallet);
        _unitOfWork.Completar();

        EnviarComprobanteAsync(wallet, tx, "completed");

        resultado.ReturnValue = new TRetiroResultado { TransactionId = tx.Id, NewBalance = wallet.Balance, ReceiptNumber = tx.ReceiptNumber };
        return resultado;
    }

    // ── Tarjeta (pendiente, procesado por admin) ──────────────────────────────

    private Response<TRetiroResultado> ProcesarTarjetaPendiente(
        Wallet wallet, PaymentMethod metodo, decimal amount)
    {
        var resultado = new Response<TRetiroResultado>();

        var destino = $"•••• •••• •••• {metodo.LastFour}";
        var tx      = CrearTransaccion(wallet, "card", destino, amount);
        tx.Status   = "pending";

        _unitOfWork.WalletTransactions.Insertar(tx);

        wallet.Balance   = Math.Round(wallet.Balance - amount, 2);
        wallet.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Wallets.Modificar(wallet);

        _unitOfWork.Completar();

        EnviarComprobanteAsync(wallet, tx, "pending");

        resultado.ReturnValue = new TRetiroResultado { TransactionId = tx.Id, NewBalance = wallet.Balance, ReceiptNumber = tx.ReceiptNumber };
        return resultado;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnviarComprobanteAsync(Wallet wallet, WalletTransaction tx, string estado)
    {
        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == wallet.UserId).ReturnValue;
        if (perfil is null || string.IsNullOrEmpty(perfil.Email)) return;

        var remitente = _config["Mail:Remitente"] ?? _config["Mail:Usuario"]!;
        _ = Task.Run(async () =>
        {
            try
            {
                await _emailService.EnviarComprobanteTransaccionAsync(
                    correoDestino:   perfil.Email,
                    nombre:          perfil.DisplayName ?? perfil.Username,
                    receiptNumber:   tx.ReceiptNumber ?? "–",
                    tipo:            "withdrawal",
                    monto:           tx.Amount,
                    balanceAntes:    tx.BalanceBefore,
                    balanceDespues:  tx.BalanceAfter,
                    estado:          estado,
                    fecha:           tx.CreatedAt,
                    correoRemitente: remitente);
            }
            catch { /* no propagar */ }
        });
    }

    private static WalletTransaction CrearTransaccion(
        Wallet wallet, string referenceType, string metadata, decimal amount) => new()
    {
        Id              = Guid.NewGuid(),
        WalletId        = wallet.Id,
        TransactionType = "withdrawal",
        Status          = "pending",
        Amount          = amount,
        BalanceBefore   = wallet.Balance,
        BalanceAfter    = Math.Round(wallet.Balance - amount, 2),
        ReferenceType   = referenceType,
        Metadata        = metadata,
        ReceiptNumber   = ReceiptGenerator.Generate(),
        CreatedAt       = DateTime.UtcNow,
    };

    // ── PayPal HTTP ───────────────────────────────────────────────────────────

    private async Task<string> ObtenerTokenPayPalAsync()
    {
        var clientId     = _config["Payments:PayPal:ClientId"]!;
        var clientSecret = _config["Payments:PayPal:ClientSecret"]!;
        var baseUrl      = _config["Payments:PayPal:BaseUrl"]!;

        var http        = _httpFactory.CreateClient();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var body     = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")]);
        var response = await http.PostAsync($"{baseUrl}/v1/oauth2/token", body);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<string> EnviarPayoutPayPalAsync(decimal amount, string receiverEmail, string senderItemId, string token)
    {
        var baseUrl = _config["Payments:PayPal:BaseUrl"]!;
        var http    = _httpFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var body = new
        {
            sender_batch_header = new
            {
                sender_batch_id = $"retiro_{senderItemId}",
                email_subject   = "Retiro de Biozin Royale",
                email_message   = "Tu retiro fue procesado exitosamente.",
            },
            items = new[]
            {
                new
                {
                    recipient_type = "EMAIL",
                    amount         = new { value = amount.ToString("F2", CultureInfo.InvariantCulture), currency = "USD" },
                    note           = "Retiro Biozin Royale",
                    receiver       = receiverEmail,
                    sender_item_id = senderItemId,
                },
            },
        };

        var content = new StringContent(JsonSerializer.Serialize(body));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var response = await http.PostAsync($"{baseUrl}/v1/payments/payouts", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement
            .GetProperty("batch_header").GetProperty("payout_batch_id").GetString()!;
    }
}
