using System.Linq.Expressions;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesAD;

public interface IRepositoryAD<T> where T : class
{
    Response<T> ObtenerEntidad(Expression<Func<T, bool>> filtro);
    Response<IEnumerable<T>> ObtenerEntidades(Expression<Func<T, bool>> filtro);
    Response<IEnumerable<T>> Listar();
    void Insertar(T entidad);
    void Modificar(T entidad);
    void Eliminar(T entidad);
}