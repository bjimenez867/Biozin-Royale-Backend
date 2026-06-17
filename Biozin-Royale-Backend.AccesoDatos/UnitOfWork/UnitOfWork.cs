using Biozin_Royale_Backend.AccesoDatos.Contexto;
using Biozin_Royale_Backend.AccesoDatos.Repositorios.Implementaciones;
using Biozin_Royale_Backend.AccesoDatos.Repositorios.Interfaces;

namespace Biozin_Royale_Backend.AccesoDatos.UnitOfWork
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _contexto;
        private readonly Dictionary<Type, object> _repositorios = new();

        public UnitOfWork(ApplicationDbContext contexto) => _contexto = contexto;

        public IRepositorioGenerico<T> Repositorio<T>() where T : class
        {
            if (!_repositorios.ContainsKey(typeof(T)))
                _repositorios[typeof(T)] = new RepositorioGenerico<T>(_contexto);

            return (IRepositorioGenerico<T>)_repositorios[typeof(T)];
        }

        public async Task<int> GuardarCambiosAsync() => await _contexto.SaveChangesAsync();

        public void Dispose() => _contexto.Dispose();
    }
}