using Biozin_Royale_Backend.Dominio.Entities;

namespace Biozin_Royale_Backend.Dominio.InterfacesAD;

public interface IUnitWork : IDisposable
{
    IRepositoryAD<Profile> Profiles { get; }
    IRepositoryAD<Wallet> Wallets { get; }
    IRepositoryAD<UserStatistics> Statistics { get; }
    IRepositoryAD<GamesHistory> GamesHistory { get; }
    IRepositoryAD<Promotion> Promotions { get; }
    IRepositoryAD<PromotionClaim> PromotionClaims { get; }
    int Completar();
    Task InsertarUsuarioAuthAsync(Guid id, string email);
    Task<bool> ExisteUsuarioAuthAsync(string email);
}