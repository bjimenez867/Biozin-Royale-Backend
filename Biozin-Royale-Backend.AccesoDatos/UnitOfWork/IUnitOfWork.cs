using Biozin_Royale_Backend.AccesoDatos.Repositorios.Interfaces;

namespace Biozin_Royale_Backend.AccesoDatos.UnitOfWork
{
    public interface IUnitOfWork : IDisposable
    {
        IRepositorioGenerico<T> Repositorio<T>() where T : class;
        Task<int> GuardarCambiosAsync();
    }
}