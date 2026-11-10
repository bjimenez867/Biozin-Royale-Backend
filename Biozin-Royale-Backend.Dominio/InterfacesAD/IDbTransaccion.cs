namespace Biozin_Royale_Backend.Dominio.InterfacesAD;

/// Transacción de base de datos expuesta a la lógica de negocio sin acoplarla a EF.
/// Si se libera sin Commit, se revierte.
public interface IDbTransaccion : IAsyncDisposable
{
    Task CommitAsync();
    Task RollbackAsync();
}
