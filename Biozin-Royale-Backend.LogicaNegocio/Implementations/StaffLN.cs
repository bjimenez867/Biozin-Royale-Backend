using Microsoft.Extensions.Configuration;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class StaffLN : IStaffLN
{
    private static readonly string[] RolesValidos = { "admin", "soporte" };

    private readonly IUnitWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;

    public StaffLN(IUnitWork unitOfWork, IConfiguration configuration, IEmailService emailService)
    {
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _emailService = emailService;
    }

    public async Task<Response<TPerfilResultado>> CrearMiembroAsync(TCrearStaffMember datos, Guid creadoPorId)
    {
        var resultado = new Response<TPerfilResultado>();

        var rol = datos.Role.Trim().ToLowerInvariant();
        if (!RolesValidos.Contains(rol))
        {
            resultado.lpError("Rol inválido", "El rol debe ser 'admin' o 'soporte'.");
            return resultado;
        }

        if (string.IsNullOrWhiteSpace(datos.Nombre) || string.IsNullOrWhiteSpace(datos.CorreoContacto))
        {
            resultado.lpError("Datos inválidos", "El nombre y el correo de contacto son obligatorios.");
            return resultado;
        }

        var baseEmail = CredentialsGenerator.GenerateBaseEmailWithFullName(datos.Nombre);
        var email = GenerarEmailUnico(baseEmail, rol);
        var username = GenerarUsernameUnico(datos.Nombre);
        var passwordTemporal = CredentialsGenerator.GeneratePassword();
        var ahora = DateTime.UtcNow;

        var staff = new StaffMember
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = datos.Nombre,
            Email = email,
            Phone = datos.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(passwordTemporal),
            Role = rol,
            Status = "active",
            CreatedBy = creadoPorId,
            CreatedAt = ahora,
            UpdatedAt = ahora
        };

        _unitOfWork.StaffMembers.Insertar(staff);
        _unitOfWork.Completar();

        try
        {
            await _emailService.EnviarCredencialesStaffAsync(
                correoDestino: datos.CorreoContacto,
                nombre: datos.Nombre,
                correoEmpresarial: email,
                password: passwordTemporal,
                rol: rol == "admin" ? "Administrador" : "Soporte",
                correoRemitente: _configuration["Mail:Remitente"] ?? "no-reply@biozinroyale.com");
        }
        catch (Exception)
        {
            // El miembro ya quedó creado; si Mailtrap falla, la pantalla de "credenciales
            // creadas" del frontend sigue mostrando la contraseña en claro como respaldo.
        }

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null, tempPassword: passwordTemporal);
        return resultado;
    }

    public Task<Response<IEnumerable<TPerfilResultado>>> ListarMiembrosAsync()
    {
        var resultado = new Response<IEnumerable<TPerfilResultado>>();

        var miembros = _unitOfWork.StaffMembers.Listar().ReturnValue!
            .OrderByDescending(s => s.CreatedAt);

        resultado.ReturnValue = miembros.Select(s => StaffMapper.MapearComoPerfil(s, token: null));
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> LoginAsync(string email, string password)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Email == email).ReturnValue;
        if (staff is null || !BCrypt.Net.BCrypt.Verify(password, staff.PasswordHash))
        {
            resultado.lpError("Credenciales inválidas", "El correo o la contraseña son incorrectos.");
            return Task.FromResult(resultado);
        }

        var token = JwtTokenFactory.GenerarToken(_configuration, staff.Id, staff.Email, staff.Role);
        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token);
        return Task.FromResult(resultado);
    }

    private string GenerarEmailUnico(string baseEmail, string rol)
    {
        var sufijo = 0;
        while (true)
        {
            var candidato = rol == "admin"
                ? CredentialsGenerator.BuildEmailAdmin(baseEmail, sufijo)
                : CredentialsGenerator.BuildSupportEmail(baseEmail, sufijo);

            var enUso = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Email == candidato).ReturnValue;
            if (enUso is null)
                return candidato;
            sufijo++;
        }
    }

    private string GenerarUsernameUnico(string nombreBase)
    {
        var sufijo = 0;
        while (true)
        {
            var candidato = CredentialsGenerator.GenerateUsername(nombreBase, sufijo);
            var enUso = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Username == candidato).ReturnValue;
            if (enUso is null)
                return candidato;
            sufijo++;
        }
    }
}
