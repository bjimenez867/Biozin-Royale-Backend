using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Biozin_Royale_Backend.Dominio.InterfacesLN;

namespace Biozin_Royale_Backend.API.Controllers;

[Authorize(Roles = "admin")]
[ApiController]
[Route("api/reportes")]
public class ReportesController : ControllerBase
{
    private readonly IReportesLN _reportesLN;

    public ReportesController(IReportesLN reportesLN)
    {
        _reportesLN = reportesLN;
    }

    [HttpGet("kpi")]
    public async Task<IActionResult> GetKpi()
    {
        var resultado = await _reportesLN.GetKpiAsync();
        return resultado.blnError ? BadRequest(resultado) : Ok(resultado);
    }

    [HttpGet("pdf")]
    public async Task<IActionResult> DescargarPdf([FromQuery] string period = "d")
    {
        if (!IsValidPeriod(period)) return BadRequest("Período inválido. Use: d, w, m, y");
        var resultado = await _reportesLN.GenerarPdfAsync(period);
        if (resultado.blnError) return BadRequest(resultado);

        var filename = BuildFilename(period, "pdf");
        return File(resultado.ReturnValue!, "application/pdf", filename);
    }

    [HttpGet("excel")]
    public async Task<IActionResult> DescargarExcel([FromQuery] string period = "d")
    {
        if (!IsValidPeriod(period)) return BadRequest("Período inválido. Use: d, w, m, y");
        var resultado = await _reportesLN.GenerarExcelAsync(period);
        if (resultado.blnError) return BadRequest(resultado);

        var filename = BuildFilename(period, "xlsx");
        return File(resultado.ReturnValue!,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            filename);
    }

    private static bool IsValidPeriod(string p) => p is "d" or "w" or "m" or "y";

    private static string BuildFilename(string period, string ext)
    {
        var hoy = DateTime.UtcNow;
        var suffix = period switch
        {
            "w" => $"semanal-{hoy:yyyy-MM-dd}",
            "m" => $"mensual-{hoy:yyyy-MM}",
            "y" => $"anual-{hoy:yyyy}",
            _   => $"diario-{hoy:yyyy-MM-dd}",
        };
        return $"reporte-{suffix}.{ext}";
    }
}
