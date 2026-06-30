using Biozin_Royale_Backend.Dominio.Entities;

namespace Biozin_Royale_Backend.Dominio.InterfacesAD;

public interface IUnitWork : IDisposable
{
    IRepositoryAD<Profile> Profiles { get; }
    IRepositoryAD<UserStatistics> Statistics { get; }
    IRepositoryAD<GamesHistory> GamesHistory { get; }
    IRepositoryAD<StaffMember> StaffMembers { get; }
    int Completar();
    Task InsertarUsuarioAuthAsync(Guid id, string email);
    Task<bool> ExisteUsuarioAuthAsync(string email);
}