using Biozin_Royale_Backend.Dominio.InterfacesLN;
using MimeKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations
{

    public class EmailService : IEmailService
    {

        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        
        
        public async Task EnviarCredencialesStaffAsync(
            string correoDestino,
            string nombre,
            string correoEmpresarial,
            string password,
            string rol,
            string correoRemitente)
        {
            var mensaje = new MimeMessage();

            mensaje.From.Add(new MailboxAddress("Biozin Royale", correoRemitente));

            mensaje.To.Add(MailboxAddress.Parse(correoDestino));

            mensaje.Subject = "Credenciales de acceso a Biozin Royale";

            var builder = new BodyBuilder();

            string ruta = Path.Combine(
                Directory.GetCurrentDirectory(),
                "EmailTemplates",
                "Credentials.html"
            );

            string html = File.ReadAllText(ruta);

            var rutaLogo = Path.Combine(Directory.GetCurrentDirectory(), "EmailTemplates", "Biozin_logo.png");
            if (File.Exists(rutaLogo))
            {
                var imagen = builder.LinkedResources.Add(rutaLogo);
                imagen.ContentId = "logo-biozin";
                html = html.Replace("{logoUrl}", $"cid:{imagen.ContentId}");
            }
            else
            {
                html = html.Replace("{logoUrl}", string.Empty);
            }

            html = html.Replace("{nombre}", nombre);
            html = html.Replace("{correoEmpresarial}", correoEmpresarial);
            html = html.Replace("{password}", password);
            html = html.Replace("{rol}", rol);
            html = html.Replace("{anio}", DateTime.Now.Year.ToString());

            builder.HtmlBody = html;

            mensaje.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

            // Sin este timeout, un Mailtrap lento o inalcanzable cuelga la creación del
            // miembro de staff indefinidamente: el miembro ya quedó persistido en BD antes
            // de llegar aquí, así que vale más fallar rápido que bloquear la respuesta HTTP.
            await smtp.ConnectAsync(
                _config["Mail:Smtp"]!,
                (int.TryParse(_config["Mail:Puerto"], out var smtpPuerto) ? smtpPuerto : 587),
                MailKit.Security.SecureSocketOptions.StartTls,
                cts.Token
            );

            await smtp.AuthenticateAsync(
                _config["Mail:Usuario"]!,
                _config["Mail:Password"]!,
                cts.Token
            );

            await smtp.SendAsync(mensaje, cts.Token);

            await smtp.DisconnectAsync(true, cts.Token);
        }

        public async Task EnviarCodigoRecuperacionAsync(
            string correoDestino,
            string nombre,
            string codigo,
            string correoRemitente)
        {
            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress("Biozin Royale", correoRemitente));
            mensaje.To.Add(MailboxAddress.Parse(correoDestino));
            mensaje.Subject = "Código de recuperación – Biozin Royale";

            var builder = new BodyBuilder();

            string ruta = Path.Combine(Directory.GetCurrentDirectory(), "EmailTemplates", "PasswordReset.html");
            string html = File.ReadAllText(ruta);

            var rutaLogo = Path.Combine(Directory.GetCurrentDirectory(), "EmailTemplates", "Biozin_logo.png");
            if (File.Exists(rutaLogo))
            {
                var imagen = builder.LinkedResources.Add(rutaLogo);
                imagen.ContentId = "logo-biozin";
                html = html.Replace("{logoUrl}", $"cid:{imagen.ContentId}");
            }
            else
            {
                html = html.Replace("{logoUrl}", string.Empty);
            }

            html = html.Replace("{nombre}", nombre);
            html = html.Replace("{codigo}", codigo);
            html = html.Replace("{anio}", DateTime.Now.Year.ToString());

            builder.HtmlBody = html;
            mensaje.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

            await smtp.ConnectAsync(
                _config["Mail:Smtp"]!,
                (int.TryParse(_config["Mail:Puerto"], out var smtpPuerto) ? smtpPuerto : 587),
                MailKit.Security.SecureSocketOptions.StartTls,
                cts.Token
            );
            await smtp.AuthenticateAsync(_config["Mail:Usuario"]!, _config["Mail:Password"]!, cts.Token);
            await smtp.SendAsync(mensaje, cts.Token);
            await smtp.DisconnectAsync(true, cts.Token);
        }

        public async Task EnviarVerificacionEmailAsync(
            string correoDestino,
            string nombre,
            string codigo,
            string correoRemitente)
        {
            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress("Biozin Royale", correoRemitente));
            mensaje.To.Add(MailboxAddress.Parse(correoDestino));
            mensaje.Subject = "Verifica tu correo – Biozin Royale";

            var builder = new BodyBuilder();

            string ruta = Path.Combine(Directory.GetCurrentDirectory(), "EmailTemplates", "EmailVerification.html");
            string html = File.ReadAllText(ruta);

            var rutaLogo = Path.Combine(Directory.GetCurrentDirectory(), "EmailTemplates", "Biozin_logo.png");
            if (File.Exists(rutaLogo))
            {
                var imagen = builder.LinkedResources.Add(rutaLogo);
                imagen.ContentId = "logo-biozin";
                html = html.Replace("{logoUrl}", $"cid:{imagen.ContentId}");
            }
            else
            {
                html = html.Replace("{logoUrl}", string.Empty);
            }

            html = html.Replace("{nombre}", nombre);
            html = html.Replace("{codigo}", codigo);
            html = html.Replace("{anio}", DateTime.Now.Year.ToString());

            builder.HtmlBody = html;
            mensaje.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

            await smtp.ConnectAsync(
                _config["Mail:Smtp"]!,
                (int.TryParse(_config["Mail:Puerto"], out var smtpPuerto) ? smtpPuerto : 587),
                MailKit.Security.SecureSocketOptions.StartTls,
                cts.Token
            );
            await smtp.AuthenticateAsync(_config["Mail:Usuario"]!, _config["Mail:Password"]!, cts.Token);
            await smtp.SendAsync(mensaje, cts.Token);
            await smtp.DisconnectAsync(true, cts.Token);
        }

        public async Task EnviarCodigo2FAAsync(
            string correoDestino,
            string nombre,
            string codigo,
            string correoRemitente)
        {
            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress("Biozin Royale", correoRemitente));
            mensaje.To.Add(MailboxAddress.Parse(correoDestino));
            mensaje.Subject = "Código de acceso – Biozin Royale";

            var builder = new BodyBuilder();

            string ruta = Path.Combine(Directory.GetCurrentDirectory(), "EmailTemplates", "TwoFactorLogin.html");
            string html = File.ReadAllText(ruta);

            var rutaLogo = Path.Combine(Directory.GetCurrentDirectory(), "EmailTemplates", "Biozin_logo.png");
            if (File.Exists(rutaLogo))
            {
                var imagen = builder.LinkedResources.Add(rutaLogo);
                imagen.ContentId = "logo-biozin";
                html = html.Replace("{logoUrl}", $"cid:{imagen.ContentId}");
            }
            else
            {
                html = html.Replace("{logoUrl}", string.Empty);
            }

            html = html.Replace("{nombre}", nombre);
            html = html.Replace("{codigo}", codigo);
            html = html.Replace("{anio}", DateTime.Now.Year.ToString());

            builder.HtmlBody = html;
            mensaje.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

            await smtp.ConnectAsync(
                _config["Mail:Smtp"]!,
                (int.TryParse(_config["Mail:Puerto"], out var smtpPuerto) ? smtpPuerto : 587),
                MailKit.Security.SecureSocketOptions.StartTls,
                cts.Token
            );
            await smtp.AuthenticateAsync(_config["Mail:Usuario"]!, _config["Mail:Password"]!, cts.Token);
            await smtp.SendAsync(mensaje, cts.Token);
            await smtp.DisconnectAsync(true, cts.Token);
        }

        public async Task EnviarComprobanteTransaccionAsync(
            string correoDestino,
            string nombre,
            string receiptNumber,
            string tipo,
            decimal monto,
            decimal balanceAntes,
            decimal balanceDespues,
            string estado,
            DateTime fecha,
            string correoRemitente)
        {
            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress("Biozin Royale", correoRemitente));
            mensaje.To.Add(MailboxAddress.Parse(correoDestino));
            mensaje.Subject = $"Comprobante {receiptNumber} – Biozin Royale";

            var builder = new BodyBuilder();

            string ruta = Path.Combine(Directory.GetCurrentDirectory(), "EmailTemplates", "TransactionReceipt.html");
            string html = File.ReadAllText(ruta);

            var rutaLogo = Path.Combine(Directory.GetCurrentDirectory(), "EmailTemplates", "Biozin_logo.png");
            if (File.Exists(rutaLogo))
            {
                var imagen = builder.LinkedResources.Add(rutaLogo);
                imagen.ContentId = "logo-biozin";
                html = html.Replace("{logoUrl}", $"cid:{imagen.ContentId}");
            }
            else
            {
                html = html.Replace("{logoUrl}", string.Empty);
            }

            var tipoLabel  = tipo == "deposit" ? "Depósito" : "Retiro";
            var estadoLabel = estado switch
            {
                "completed" => "Completado",
                "pending"   => "En proceso",
                "failed"    => "Fallido",
                _           => estado,
            };
            var colorEstado = estado switch
            {
                "completed" => "#4fd190",
                "pending"   => "#e7c86b",
                _           => "#e06a6a",
            };
            var signo = tipo == "deposit" ? "+" : "-";

            html = html.Replace("{nombre}",          nombre);
            html = html.Replace("{receiptNumber}",   receiptNumber);
            html = html.Replace("{tipo}",            tipoLabel);
            html = html.Replace("{monto}",           $"{signo}${Math.Abs(monto):F2}");
            html = html.Replace("{balanceAntes}",    $"${balanceAntes:F2}");
            html = html.Replace("{balanceDespues}",  $"${balanceDespues:F2}");
            html = html.Replace("{estado}",          estadoLabel);
            html = html.Replace("{colorEstado}",     colorEstado);
            html = html.Replace("{fecha}",           fecha.ToString("dd MMM yyyy · HH:mm 'UTC'", new System.Globalization.CultureInfo("es-CR")));
            html = html.Replace("{anio}",            DateTime.Now.Year.ToString());

            builder.HtmlBody = html;
            mensaje.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await smtp.ConnectAsync(
                _config["Mail:Smtp"]!,
                (int.TryParse(_config["Mail:Puerto"], out var smtpPuerto) ? smtpPuerto : 587),
                MailKit.Security.SecureSocketOptions.StartTls,
                cts.Token);
            await smtp.AuthenticateAsync(_config["Mail:Usuario"]!, _config["Mail:Password"]!, cts.Token);
            await smtp.SendAsync(mensaje, cts.Token);
            await smtp.DisconnectAsync(true, cts.Token);
        }

        public async Task EnviarAutoReplyTicketAsync(
            string correoDestino,
            string nombre,
            int ticketNumber,
            string categoria,
            string correoRemitente)
        {
            var mensaje = new MimeMessage();
            mensaje.From.Add(new MailboxAddress("Biozin Royale Soporte", correoRemitente));
            mensaje.To.Add(MailboxAddress.Parse(correoDestino));
            mensaje.Subject = $"Ticket #BR-{ticketNumber} creado – Biozin Royale";

            var builder = new BodyBuilder();
            builder.TextBody =
                $"Hola {nombre},\n\n" +
                $"Tu solicitud de soporte fue recibida y registrada con el número #BR-{ticketNumber}.\n\n" +
                $"Categoría: {categoria}\n\n" +
                $"Nuestro equipo la atenderá a la brevedad. Puedes hacer seguimiento iniciando sesión en la app.\n\n" +
                $"Equipo Biozin Royale";
            mensaje.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await smtp.ConnectAsync(
                _config["Mail:Smtp"]!,
                (int.TryParse(_config["Mail:Puerto"], out var smtpPuerto) ? smtpPuerto : 587),
                MailKit.Security.SecureSocketOptions.StartTls,
                cts.Token);
            await smtp.AuthenticateAsync(_config["Mail:Usuario"]!, _config["Mail:Password"]!, cts.Token);
            await smtp.SendAsync(mensaje, cts.Token);
            await smtp.DisconnectAsync(true, cts.Token);
        }

    }
}