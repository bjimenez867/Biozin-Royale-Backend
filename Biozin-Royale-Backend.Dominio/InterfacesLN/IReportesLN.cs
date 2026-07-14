using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.Dominio.InterfacesLN;

public interface IReportesLN
{
    Task<Response<TReportesKpiResultado>> GetKpiAsync();
    Task<Response<byte[]>> GenerarPdfAsync(string period);
    Task<Response<byte[]>> GenerarExcelAsync(string period);
}
