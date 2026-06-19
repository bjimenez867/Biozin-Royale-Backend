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

    public void Insertar(T entidad)
    {
        _dbSet.Add(entidad);
    }

    public void Modificar(T entidad)
    {
        _dbSet.Update(entidad);
    }

    public void Eliminar(T entidad)
    {
        _dbSet.Remove(entidad);
    }
}