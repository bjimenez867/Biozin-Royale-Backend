using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class ProfileLN : IProfileLN
{
    private readonly IUnitWork _unitOfWork;

    public ProfileLN(IUnitWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public Task<Response<TPerfilResultado>> ObtenerPerfilAsync(Guid userId)
    {
        var resultado = new Response<TPerfilResultado>();
        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return Task.FromResult(resultado);
        }

        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, token: null);
        return Task.FromResult(resultado);
    }

    public Task<Response<TPerfilResultado>> ActualizarPerfilAsync(Guid userId, TActualizarPerfil datos)
    {
        var resultado = new Response<TPerfilResultado>();
        var perfil = _unitOfWork.Profiles.ObtenerEntidad(p => p.UserId == userId).ReturnValue;
        if (perfil is null)
        {
            resultado.lpError("Perfil no encontrado", "No existe un perfil asociado a esta sesión.");
            return Task.FromResult(resultado);
        }

        if (!string.IsNullOrWhiteSpace(datos.Username) && datos.Username != perfil.Username)
        {
            var enUso = _unitOfWork.Profiles.ObtenerEntidad(p => p.Username == datos.Username).ReturnValue;
            if (enUso is not null)
            {
                resultado.lpError("Usuario en uso", "Ese nombre de usuario ya está ocupado.");
                return Task.FromResult(resultado);
            }
            perfil.Username = datos.Username;
        }

        if (datos.DisplayName is not null) perfil.DisplayName = datos.DisplayName;
        if (datos.Phone is not null) perfil.Phone = datos.Phone;
        if (datos.Country is not null) perfil.Country = datos.Country;
        if (datos.Birthdate is not null) perfil.Birthdate = datos.Birthdate;
        perfil.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Profiles.Modificar(perfil);
        _unitOfWork.Completar();

        resultado.ReturnValue = PerfilMapper.MapearPerfil(perfil, token: null);
        return Task.FromResult(resultado);
    }

    public Task<Response<TEstadisticas>> ObtenerEstadisticasAsync(Guid userId)
    {
        var resultado = new Response<TEstadisticas>();
        var stats = _unitOfWork.Statistics.ObtenerEntidad(s => s.UserId == userId).ReturnValue;

        // Sin filas en bets para este usuario (aún no jugó): se devuelven ceros, no error.
        resultado.ReturnValue = stats is null
            ? new TEstadisticas()
            : new TEstadisticas
            {
                PartidasJugadas = stats.PartidasJugadas,
                PartidasGanadas = stats.PartidasGanadas,
                ApostadoTotal = stats.ApostadoTotal,
                GananciasNetas = stats.GananciasNetas
            };
        return Task.FromResult(resultado);
    }
}
