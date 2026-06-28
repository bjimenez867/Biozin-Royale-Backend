using Microsoft.EntityFrameworkCore;
using Biozin_Royale_Backend.AccesoDatos.Contexto;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;

namespace Biozin_Royale_Backend.AccesoDatos.Repositories.Implementaciones;

public class UnitWorkEF : IUnitWork
{
    private readonly ApplicationDbContext _contexto;

    public IRepositoryAD<Profile> Profiles { get; }
    public IRepositoryAD<Wallet> Wallets { get; }
    public IRepositoryAD<UserStatistics> Statistics { get; }
    public IRepositoryAD<GamesHistory> GamesHistory { get; }

    public UnitWorkEF(ApplicationDbContext contexto)
    {
        _contexto = contexto;
        Profiles = new RepositoryAD<Profile>(contexto);
        Wallets = new RepositoryAD<Wallet>(contexto);
        Statistics = new RepositoryAD<UserStatistics>(contexto);
        GamesHistory = new RepositoryAD<GamesHistory>(contexto);
    }

    public int Completar()
    {
        return _contexto.SaveChanges();
    }

    /// auth.users es la tabla interna de Supabase Auth; no se mapea como entidad EF
    /// porque el esquema lo administra Supabase, no este proyecto. Solo se inserta
    /// la fila mínima necesaria para satisfacer la FK de profiles.user_id en el
    /// registro manual (sin pasar por la API de GoTrue/Supabase Auth).
    /// GoTrue (el servicio de Auth de Supabase) espera estas columnas de tokens como
    /// '' (string vacío), nunca NULL: si quedan en NULL, cualquier operación de Auth
    /// que escanee esta fila (ej. login con Google buscando si el email ya existe)
    /// falla con "converting NULL to string is unsupported".
    /// instance_id tampoco puede quedar en NULL por la misma razón: GoTrue lo escanea
    /// como UUID no-nullable. Se usa el UUID cero, la convención de Supabase cuando no
    /// hay múltiples instancias.
    private static readonly Guid InstanciaGoTrue = Guid.Empty;

    public async Task InsertarUsuarioAuthAsync(Guid id, string email)
    {
        await _contexto.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO auth.users (
                id, instance_id, aud, role, email, created_at, updated_at, is_sso_user, is_anonymous,
                confirmation_token, recovery_token, email_change_token_new,
                email_change_token_current, email_change, phone_change,
                phone_change_token, reauthentication_token
            )
            VALUES (
                {id}, {InstanciaGoTrue}, 'authenticated', 'authenticated', {email}, now(), now(), false, false,
                '', '', '', '', '', '', '', ''
            )");
    }

    /// Verifica si ya existe un usuario en auth.users con este correo (no-SSO), sin
    /// importar si tiene o no un Profile asociado. Necesario porque un login con Google
    /// puede dejar una fila en auth.users sin Profile (si el sync nunca se completó), y
    /// el registro manual chocaría con la restricción "users_email_partial_key" al
    /// intentar insertar el mismo correo de nuevo.
    public async Task<bool> ExisteUsuarioAuthAsync(string email)
    {
        var conteo = await _contexto.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM auth.users WHERE email = {0} AND is_sso_user = false",
                email)
            .SingleAsync();
        return conteo > 0;
    }

    public void Dispose()
    {
        _contexto.Dispose();
    }
}
