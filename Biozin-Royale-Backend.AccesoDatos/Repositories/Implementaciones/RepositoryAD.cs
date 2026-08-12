using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.AccesoDatos.Repositories.Implementaciones;

public class RepositoryAD<T> : IRepositoryAD<T> where T : class
{
    private readonly DbContext _contexto;
    private readonly DbSet<T> _dbSet;

    public RepositoryAD(DbContext contexto)
    {
        _contexto = contexto;
        _dbSet = contexto.Set<T>();
    }

    public Response<T> ObtenerEntidad(Expression<Func<T, bool>> filtro)
    {
        var resultado = new Response<T>();
        resultado.ReturnValue = _dbSet.FirstOrDefault(filtro)!;
        return resultado;
    }

    public Response<IEnumerable<T>> ObtenerEntidades(Expression<Func<T, bool>> filtro)
    {
        var resultado = new Response<IEnumerable<T>>();
        resultado.ReturnValue = _dbSet.Where(filtro).ToList();
        return resultado;
    }

    public Response<IEnumerable<T>> Listar()
    {
        var resultado = new Response<IEnumerable<T>>();
        resultado.ReturnValue = _dbSet.ToList();
        return resultado;
    }

    public async Task<T?> ObtenerEntidadAsync(Expression<Func<T, bool>> filtro)
        => await _dbSet.AsNoTracking().FirstOrDefaultAsync(filtro);

    public async Task<List<T>> ObtenerEntidadesAsync(Expression<Func<T, bool>> filtro)
        => await _dbSet.AsNoTracking().Where(filtro).ToListAsync();

    public async Task<List<T>> ListarAsync()
        => await _dbSet.AsNoTracking().ToListAsync();

    public void Insertar(T entidad)
    {
        _dbSet.Add(entidad);
    }

    public void Modificar(T entidad)
    {
        // Las lecturas async usan AsNoTracking, así que dentro de un mismo request
        // pueden convivir dos instancias de la misma fila: una trackeada (recién
        // insertada/modificada) y otra sin trackear (releída después). Update()
        // sobre la segunda lanza InvalidOperationException por conflicto de
        // identidad. Si ya hay OTRA instancia trackeada con la misma clave, se
        // copian los valores sobre ella en vez de adjuntar la nueva.
        var trackeada = BuscarOtraInstanciaTrackeada(entidad);
        if (trackeada is not null)
        {
            trackeada.CurrentValues.SetValues(entidad);
            return;
        }

        _dbSet.Update(entidad);
    }

    public void Eliminar(T entidad)
    {
        // Mismo conflicto de identidad que en Modificar: si otra instancia de la
        // misma fila ya está trackeada, se elimina ESA en vez de adjuntar la nueva.
        var trackeada = BuscarOtraInstanciaTrackeada(entidad);
        if (trackeada is not null)
        {
            _dbSet.Remove(trackeada.Entity);
            return;
        }

        _dbSet.Remove(entidad);
    }

    /// Entrada del change tracker que apunta a la misma fila (misma clave primaria)
    /// pero con una instancia DISTINTA a la recibida; null si no existe.
    private Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T>? BuscarOtraInstanciaTrackeada(T entidad)
    {
        var claves = _contexto.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties;
        var valoresClave = claves.Select(p => p.PropertyInfo!.GetValue(entidad)).ToArray();

        var trackeada = _contexto.ChangeTracker.Entries<T>().FirstOrDefault(e =>
            claves.Select(p => e.Property(p.Name).CurrentValue).SequenceEqual(valoresClave));

        return trackeada is not null && !ReferenceEquals(trackeada.Entity, entidad)
            ? trackeada
            : null;
    }
}