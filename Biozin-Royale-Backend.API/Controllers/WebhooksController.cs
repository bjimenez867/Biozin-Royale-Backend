using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Biozin_Royale_Backend.API.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IDepositosLN  _depositosLN;
    private readonly ITicketsLN    _ticketsLN;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;

    public WebhooksController(
        IDepositosLN depositosLN,
        ITicketsLN ticketsLN,
        IEmailService emailService,
        IConfiguration config)
    {
        _depositosLN  = depositosLN;
        _ticketsLN    = ticketsLN;
        _emailService = emailService;
        _config       = config;
    }

    [HttpPost("stripe")]
    public async Task<IActionResult> StripeWebhook()
    {
        var payload   = await new StreamReader(Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"].ToString();

        var resultado = await _depositosLN.ProcesarWebhookStripeAsync(payload, signature);
        return resultado.blnError ? BadRequest() : Ok();
    }

    // POST api/webhooks/email  — Mailgun inbound parse
    [HttpPost("email")]
    [Consumes("multipart/form-data", "application/x-www-form-urlencoded")]
    public async Task<IActionResult> EmailWebhook([FromForm] MailgunPayload payload)
    {
        var signingKey = _config["Mailgun:WebhookSigningKey"];
        if (string.IsNullOrEmpty(signingKey) || !VerifyMailgunSignature(signingKey, payload))
            return Unauthorized();

        // Extraer email del remitente ("Nombre <email>" o "email")
        var fromEmail = ExtractEmail(payload.From);
        if (string.IsNullOrEmpty(fromEmail))
            return BadRequest();

        // Extraer categoría del asunto: "[Soporte] Pagos - Juan"
        var category = ParseCategory(payload.Subject);
        var body     = (payload.StrippedText ?? payload.BodyPlain ?? "").Trim();
        var fromName = ExtractName(payload.From) ?? fromEmail;

        var correoRemitente = _config["Mail:Usuario"] ?? "soporte@biozinroyale.com";

        // Buscar usuario por correo en el sistema
        var resultado = await _ticketsLN.CrearDesdeEmailAsync(fromEmail, fromName, category, body);

        if (resultado.blnError || resultado.ReturnValue is null)
            return Ok(); // Mailgun espera 200 siempre para no reintentar

        // Auto-reply con número de ticket
        try
        {
            await _emailService.EnviarAutoReplyTicketAsync(
                fromEmail, fromName,
                resultado.ReturnValue.TicketNumber,
                category,
                correoRemitente);
        }
        catch { /* El auto-reply no bloquea el flujo */ }

        return Ok();
    }

    private static bool VerifyMailgunSignature(string key, MailgunPayload p)
    {
        if (p.Timestamp is null || p.Token is null || p.Signature is null)
            return false;

        var data    = p.Timestamp + p.Token;
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var hash    = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(data));
        var computed = Convert.ToHexString(hash).ToLowerInvariant();
        return computed == p.Signature;
    }

    private static string ExtractEmail(string? from)
    {
        if (string.IsNullOrEmpty(from)) return string.Empty;
        var m = Regex.Match(from, @"<([^>]+)>");
        return m.Success ? m.Groups[1].Value.Trim() : from.Trim();
    }

    private static string? ExtractName(string? from)
    {
        if (string.IsNullOrEmpty(from)) return null;
        var m = Regex.Match(from, @"^(.+?)\s*<");
        return m.Success ? m.Groups[1].Value.Trim('"', ' ') : null;
    }

    private static string ParseCategory(string? subject)
    {
        if (string.IsNullOrEmpty(subject)) return "Otro";
        // Formato: "[Soporte] Pagos - Juan"
        var m = Regex.Match(subject, @"\[Soporte\]\s+(.+?)\s+-\s+", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : "Otro";
    }
}

public class MailgunPayload
{
    [Microsoft.AspNetCore.Mvc.FromForm(Name = "from")]
    public string? From { get; set; }

    [Microsoft.AspNetCore.Mvc.FromForm(Name = "subject")]
    public string? Subject { get; set; }

    [Microsoft.AspNetCore.Mvc.FromForm(Name = "stripped-text")]
    public string? StrippedText { get; set; }

    [Microsoft.AspNetCore.Mvc.FromForm(Name = "body-plain")]
    public string? BodyPlain { get; set; }

    [Microsoft.AspNetCore.Mvc.FromForm(Name = "timestamp")]
    public string? Timestamp { get; set; }

    [Microsoft.AspNetCore.Mvc.FromForm(Name = "token")]
    public string? Token { get; set; }

    [Microsoft.AspNetCore.Mvc.FromForm(Name = "signature")]
    public string? Signature { get; set; }
}
