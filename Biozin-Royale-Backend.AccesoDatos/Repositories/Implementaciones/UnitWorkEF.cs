using Microsoft.EntityFrameworkCore;
using Biozin_Royale_Backend.AccesoDatos.Contexto;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;

namespace Biozin_Royale_Backend.AccesoDatos.Repositories.Implementaciones;

public class UnitWorkEF : IUnitWork
{
    private readonly ApplicationDbContext _contexto;

    public IRepositoryAD<Profile> Profiles { get; }

    public UnitWorkEF(ApplicationDbContext contexto)
    {
        _contexto = contexto;
        Profiles = new RepositoryAD<Profile>(contexto);
    }

    public int Completar()
    {
        return _contexto.SaveChanges();
    }

    /// auth.users es la tabla interna de Supabase Auth; no se mapea como entidad EF
    /// porque el esquema lo administra Supabase, no este proyecto. Solo se inserta
    /// la fila mínima necesaria para satisfacer la FK de profiles.user_id en el
    /// registro manual (sin pasar por la API de GoTrue/Supabase Auth).
    public async Task InsertarUsuarioAuthAsync(Guid id, string email)
    {
        await _contexto.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO auth.users (id, aud, role, email, created_at, updated_at, is_sso_user, is_anonymous)
            VALUES ({id}, 'authenticated', 'authenticated', {email}, now(), now(), false, false)");
    }

    public void Dispose()
    {
        _contexto.Dispose();
    }
}
