using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Biozin_Royale_Backend.AccesoDatos.Contexto;
using Biozin_Royale_Backend.AccesoDatos.Repositorios.Interfaces;

namespace Biozin_Royale_Backend.AccesoDatos.Repositorios.Implementaciones
{
    public class RepositorioGenerico<T> : IRepositorioGenerico<T> where T : class
    {
        protected readonly ApplicationDbContext _contexto;
        protected readonly DbSet<T> _dbSet;

        public RepositorioGenerico(ApplicationDbContext contexto)
        {
            _contexto = contexto;
            _dbSet = _contexto.Set<T>();
        }

        public async Task<T?> ObtenerPorIdAsync(object id) => await _dbSet.FindAsync(id);

        public async Task<IEnumerable<T>> ObtenerTodosAsync() => await _dbSet.ToListAsync();

        public async Task<IEnumerable<T>> ObtenerPorCondicionAsync(Expression<Func<T, bool>> condicion)
            => await _dbSet.Where(condicion).ToListAsync();

        public async Task AgregarAsync(T entidad) => await _dbSet.AddAsync(entidad);

        public void Actualizar(T entidad) => _dbSet.Update(entidad);

        public void Eliminar(T entidad) => _dbSet.Remove(entidad);
    }
}