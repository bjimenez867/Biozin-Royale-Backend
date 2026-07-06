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
            Status = "active",
            MustChangePassword = true,
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

        var token = JwtTokenFactory.GenerarToken(_configuration, staff.Id, staff.Email, CredentialsGenerator.DetectRole(staff.Email));
        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token);
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> ObtenerMeAsync(Guid staffId)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == staffId).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null);
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> ActualizarMeAsync(Guid staffId, TActualizarStaffMember datos)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == staffId).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        if (!string.IsNullOrWhiteSpace(datos.DisplayName))
            staff.DisplayName = datos.DisplayName.Trim();

        staff.Phone = string.IsNullOrWhiteSpace(datos.Phone) ? null : datos.Phone.Trim();
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        _unitOfWork.Completar();

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null);
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> ObtenerMiembroAsync(Guid id)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == id).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        string? createdByName = null;
        if (staff.CreatedBy.HasValue)
        {
            var creator = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == staff.CreatedBy.Value).ReturnValue;
            createdByName = creator?.DisplayName ?? creator?.Username;
        }

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null, createdByName: createdByName);
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> ActualizarMiembroAsync(Guid id, TActualizarStaffMember datos)
    {
        var resultado = new Response<TPerfilResultado>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == id).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        if (!string.IsNullOrWhiteSpace(datos.DisplayName))
            staff.DisplayName = datos.DisplayName.Trim();

        staff.Phone = string.IsNullOrWhiteSpace(datos.Phone) ? null : datos.Phone.Trim();
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        _unitOfWork.Completar();

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null);
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> CambiarStatusMiembroAsync(Guid id, string nuevoStatus)
    {
        var resultado = new Response<TPerfilResultado>();

        var statusNorm = nuevoStatus.Trim().ToLowerInvariant();
        if (statusNorm != "active" && statusNorm != "inactive")
        {
            resultado.lpError("Estado inválido", "El estado debe ser 'active' o 'inactive'.");
            return Task.FromResult(resultado);
        }

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == id).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        staff.Status = statusNorm;
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        _unitOfWork.Completar();

        resultado.ReturnValue = StaffMapper.MapearComoPerfil(staff, token: null);
        return Task.FromResult(resultado);
    }

    public Task<Response<bool>> EliminarMiembroAsync(Guid id)
    {
        var resultado = new Response<bool>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == id).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        _unitOfWork.StaffMembers.Eliminar(staff);
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    public Task<Response<bool>> CambiarPasswordAsync(Guid staffId, string oldPassword, string newPassword)
    {
        var resultado = new Response<bool>();

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            resultado.lpError("Contraseña inválida", "La nueva contraseña debe tener al menos 8 caracteres.");
            return Task.FromResult(resultado);
        }

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == staffId).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return Task.FromResult(resultado);
        }

        if (!BCrypt.Net.BCrypt.Verify(oldPassword, staff.PasswordHash))
        {
            resultado.lpError("Contraseña incorrecta", "La contraseña actual es incorrecta.");
            return Task.FromResult(resultado);
        }

        staff.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        staff.MustChangePassword = false;
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        _unitOfWork.Completar();

        resultado.ReturnValue = true;
        return Task.FromResult(resultado);
    }

    public async Task<Response<bool>> RestablecerPasswordStaffAsync(Guid id)
    {
        var resultado = new Response<bool>();

        var staff = _unitOfWork.StaffMembers.ObtenerEntidad(s => s.Id == id).ReturnValue;
        if (staff is null)
        {
            resultado.lpError("No encontrado", "Miembro del equipo no encontrado.");
            return resultado;
        }

        var nuevaPassword = CredentialsGenerator.GeneratePassword();
        staff.PasswordHash = BCrypt.Net.BCrypt.HashPassword(nuevaPassword);
        staff.MustChangePassword = true;
        staff.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.StaffMembers.Modificar(staff);
        _unitOfWork.Completar();

        var rol = CredentialsGenerator.DetectRole(staff.Email) == "admin" ? "Administrador" : "Soporte";
        var remitente = _configuration["Mail:Remitente"] ?? "no-reply@biozinroyale.com";

        try
        {
            await _emailService.EnviarCredencialesStaffAsync(
                correoDestino: staff.Email,
                nombre: staff.DisplayName,
                correoEmpresarial: staff.Email,
                password: nuevaPassword,
                rol: rol,
                correoRemitente: remitente);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RestablecerPasswordStaff] Error enviando correo a {staff.Email}: {ex.GetType().Name} — {ex.Message}");
        }

        resultado.ReturnValue = true;
        return resultado;
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
