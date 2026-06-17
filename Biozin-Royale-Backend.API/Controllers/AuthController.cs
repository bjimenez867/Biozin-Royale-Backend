using Microsoft.AspNetCore.Mvc;

namespace Biozin_Royale_Backend.API.Controllers;

public class AuthController
{
        /// <summary>
        /// Login unificado. Detecta el rol según el dominio del email:
        ///   @admin.biozin.com → Administrador
        ///   @suppor.biozin.edu.cr → Administrador
        /// </summary>
        [HttpPost("Login")]
        public IActionResult Login([FromBody] TLoginRequest login)
        {
            if (!ModelState.IsValid)
                return BadRequest(new Respuesta<TAuthRespuesta> { blnError = true, strTituloRespuesta = "Datos inválidos", strMensajeRespuesta = "El correo y la contraseña son obligatorios." });

            var dominio = ObtenerDominio(login.Email);

            return dominio switch
            {
                "@est.biozin.edu.cr" => LoginEstudiante(login),
                "@prof.biozin.edu.cr" => LoginProfesor(login),
                "@admin.biozin.edu.cr" => LoginAdministrador(login),
                _ => Ok(new Respuesta<TAuthRespuesta> { blnError = true, strTituloRespuesta = "Dominio no reconocido", strMensajeRespuesta = "El correo ingresado no corresponde a ningún perfil del sistema." })
            };
        }

        [HttpPost("SolicitarRecuperacion")]
        public async Task<IActionResult> SolicitarRecuperacion([FromBody] TSolicitarRecuperacion obj)
        {
            if (!ModelState.IsValid)
                return BadRequest(new Respuesta<object> { blnError = true, strTituloRespuesta = "Datos inválidos", strMensajeRespuesta = "El correo es obligatorio." });

            var dominio = ObtenerDominio(obj.Email);

            return dominio switch
            {
                "@est.biozin.edu.cr" => Ok(await _portalEstudianteLN.SolicitarRecuperacion(obj.Email)),
                "@prof.biozin.edu.cr" => Ok(await _profesorLN.SolicitarRecuperacion(obj.Email)),
                "@admin.biozin.edu.cr" => Ok(await _administradorLN.SolicitarRecuperacion(obj.Email)),
                _ => Ok(new Respuesta<object> { blnError = true, strTituloRespuesta = "Dominio no reconocido", strMensajeRespuesta = "El correo ingresado no corresponde a ningún perfil del sistema." })
            };
        }
        
        
}