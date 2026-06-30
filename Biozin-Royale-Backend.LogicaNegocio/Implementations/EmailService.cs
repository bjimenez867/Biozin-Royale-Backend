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
            html = html.Replace("{urlPanel}", _config["Mail:PanelUrl"] ?? "#");
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
        

    }
}