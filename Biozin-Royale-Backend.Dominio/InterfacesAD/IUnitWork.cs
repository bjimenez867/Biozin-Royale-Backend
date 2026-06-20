using Biozin_Royale_Backend.Dominio.Entities;

namespace Biozin_Royale_Backend.Dominio.InterfacesAD;

public interface IUnitWork : IDisposable
{
    IRepositoryAD<Profile> Profiles { get; }
    int Completar();
    Task InsertarUsuarioAuthAsync(Guid id, string email);
    Task<bool> ExisteUsuarioAuthAsync(string email);
}