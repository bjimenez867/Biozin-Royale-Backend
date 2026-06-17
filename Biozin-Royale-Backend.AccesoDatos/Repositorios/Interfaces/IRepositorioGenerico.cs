using System.Linq.Expressions;

namespace Biozin_Royale_Backend.AccesoDatos.Repositorios.Interfaces
{
    public interface IRepositorioGenerico<T> where T : class
    {
        Task<T?> ObtenerPorIdAsync(object id);
        Task<IEnumerable<T>> ObtenerTodosAsync();
        Task<IEnumerable<T>> ObtenerPorCondicionAsync(Expression<Func<T, bool>> condicion);
        Task AgregarAsync(T entidad);
        void Actualizar(T entidad);
        void Eliminar(T entidad);
    }
}