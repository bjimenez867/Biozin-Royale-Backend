using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Biozin_Royale_Backend.Dominio.Entities;
using Biozin_Royale_Backend.Dominio.InterfacesAD;
using Biozin_Royale_Backend.Dominio.InterfacesLN;
using Biozin_Royale_Backend.Dominio.TypedEntities;
using Biozin_Royale_Backend.Utilidades;

namespace Biozin_Royale_Backend.LogicaNegocio.Implementations;

public class ReportesLN : IReportesLN
{
    private readonly IUnitWork _unitOfWork;

    public ReportesLN(IUnitWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    // ── KPI del día ───────────────────────────────────────────────────────────

    public Task<Response<TReportesKpiResultado>> GetKpiAsync()
    {
        var resultado = new Response<TReportesKpiResultado>();
        var hoy = DateTime.UtcNow.Date;
        var manana = hoy.AddDays(1);

        var activeUsers = _unitOfWork.Profiles
            .ObtenerEntidades(p => !p.IsGuest && p.Status == "active")
            .ReturnValue!.Count();

        var depositos = _unitOfWork.WalletTransactions
            .ObtenerEntidades(t => t.TransactionType == "deposit"
                && t.Status == "completed"
                && t.CreatedAt >= hoy && t.CreatedAt < manana)
            .ReturnValue!.ToList();

        var retiros = _unitOfWork.WalletTransactions
            .ObtenerEntidades(t => t.TransactionType == "withdrawal"
                && t.Status == "completed"
                && t.CreatedAt >= hoy && t.CreatedAt < manana)
            .ReturnValue!.ToList();

        var depositTotal    = depositos.Sum(t => t.Amount);
        var withdrawalTotal = retiros.Sum(t => t.Amount);

        resultado.ReturnValue = new TReportesKpiResultado
        {
            ActiveUsers     = activeUsers,
            DepositTotal    = depositTotal,
            WithdrawalTotal = withdrawalTotal,
            NetProfit       = depositTotal - withdrawalTotal,
        };

        return Task.FromResult(resultado);
    }

    // ── PDF ───────────────────────────────────────────────────────────────────

    public Task<Response<byte[]>> GenerarPdfAsync(string period)
    {
        var resultado = new Response<byte[]>();
        var (from, to, label, titulo) = GetRange(period);
        var filas = GetFilas(from, to);

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(9).FontColor("#1A1A1A"));

                // ── Header ────────────────────────────────────────────────────
                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("BIOZIN ROYALE")
                                .Bold().FontSize(18).FontColor("#B8860B");
                            c.Item().Text($"Reporte Financiero · {titulo}")
                                .FontSize(11).FontColor("#444444");
                            c.Item().Text($"Período: {label}")
                                .FontSize(9).FontColor("#777777");
                        });
                        row.ConstantItem(140).AlignRight().Column(c =>
                        {
                            c.Item().Text($"Generado: {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC")
                                .FontSize(8).FontColor("#999999");
                        });
                    });
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor("#D4AF37");
                    col.Item().Height(10);
                });

                // ── Content ───────────────────────────────────────────────────
                page.Content().Column(col =>
                {
                    // Resumen
                    col.Item().PaddingBottom(4)
                        .Text("RESUMEN DEL PERÍODO").Bold().FontSize(10).FontColor("#B8860B");

                    var depositos   = filas.Where(f => f.TransactionType == "deposit"   && f.Status == "completed").ToList();
                    var retiros     = filas.Where(f => f.TransactionType == "withdrawal" && f.Status == "completed").ToList();
                    var depTotal    = depositos.Sum(f => f.Amount);
                    var retTotal    = retiros.Sum(f => f.Amount);

                    col.Item().PaddingBottom(12).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(3);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                        });

                        // Header row
                        SummaryHeaderCell(table, "Concepto");
                        SummaryHeaderCell(table, "Transacciones");
                        SummaryHeaderCell(table, "Total");

                        // Data rows
                        SummaryDataCell(table, "Depósitos completados");
                        SummaryDataCell(table, depositos.Count.ToString());
                        SummaryDataCell(table, $"${depTotal:N2}");

                        SummaryDataCell(table, "Retiros completados");
                        SummaryDataCell(table, retiros.Count.ToString());
                        SummaryDataCell(table, $"${retTotal:N2}");

                        // Net profit row highlight
                        table.Cell().BorderBottom(1).BorderColor("#D4AF37").PaddingVertical(5).PaddingHorizontal(4)
                            .Text("Ganancia neta").Bold().FontColor("#B8860B");
                        table.Cell().BorderBottom(1).BorderColor("#D4AF37").PaddingVertical(5).PaddingHorizontal(4)
                            .Text("").Bold();
                        table.Cell().BorderBottom(1).BorderColor("#D4AF37").PaddingVertical(5).PaddingHorizontal(4)
                            .Text($"${depTotal - retTotal:N2}").Bold()
                            .FontColor(depTotal >= retTotal ? "#2E7D32" : "#C62828");
                    });

                    // Transaction table
                    col.Item().PaddingBottom(4)
                        .Text("DETALLE DE TRANSACCIONES").Bold().FontSize(10).FontColor("#B8860B");

                    if (filas.Count == 0)
                    {
                        col.Item().Text("No hay transacciones en este período.")
                            .FontColor("#999999").Italic();
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(2);  // Fecha
                                cols.RelativeColumn(1.5f); // Hora
                                cols.RelativeColumn(2);  // Usuario
                                cols.RelativeColumn(2);  // Nombre
                                cols.RelativeColumn(1.5f); // Tipo
                                cols.RelativeColumn(1.5f); // Monto
                                cols.RelativeColumn(1.5f); // Estado
                            });

                            // Headers
                            foreach (var h in new[] { "Fecha", "Hora", "Usuario", "Nombre", "Tipo", "Monto", "Estado" })
                                TxHeaderCell(table, h);

                            // Rows
                            var local = TimeZoneInfo.Local;
                            foreach (var f in filas)
                            {
                                var dt = TimeZoneInfo.ConvertTimeFromUtc(f.CreatedAt, local);
                                var isDeposit = f.TransactionType == "deposit";
                                TxDataCell(table, dt.ToString("dd/MM/yyyy"));
                                TxDataCell(table, dt.ToString("HH:mm"));
                                TxDataCell(table, f.Username);
                                TxDataCell(table, f.DisplayName);
                                TxDataCell(table, isDeposit ? "Depósito" : "Retiro",
                                    isDeposit ? "#1B5E20" : "#B71C1C");
                                TxDataCell(table, $"{(isDeposit ? "+" : "-")}${f.Amount:N2}",
                                    isDeposit ? "#2E7D32" : "#C62828");
                                TxDataCell(table, StatusLabel(f.Status));
                            }
                        });
                    }
                });

                // ── Footer ────────────────────────────────────────────────────
                page.Footer().AlignCenter()
                    .Text(t =>
                    {
                        t.Span("Biozin Royale  ·  Reporte Confidencial  ·  Página ")
                            .FontSize(8).FontColor("#AAAAAA");
                        t.CurrentPageNumber().FontSize(8).FontColor("#AAAAAA");
                        t.Span(" de ").FontSize(8).FontColor("#AAAAAA");
                        t.TotalPages().FontSize(8).FontColor("#AAAAAA");
                    });
            });
        });

        resultado.ReturnValue = pdf.GeneratePdf();
        return Task.FromResult(resultado);
    }

    // ── Excel ─────────────────────────────────────────────────────────────────

    public Task<Response<byte[]>> GenerarExcelAsync(string period)
    {
        var resultado = new Response<byte[]>();
        var (from, to, label, titulo) = GetRange(period);
        var filas = GetFilas(from, to);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Reporte Financiero");

        // Estilos de colores
        var goldBg    = XLColor.FromHtml("#B8860B");
        var goldLight = XLColor.FromHtml("#FFF8E1");
        var headerBg  = XLColor.FromHtml("#1A1A2E");
        var white     = XLColor.White;
        var darkText  = XLColor.FromHtml("#1A1A1A");
        var greenText = XLColor.FromHtml("#1B5E20");
        var redText   = XLColor.FromHtml("#B71C1C");

        int row = 1;

        // Título
        ws.Cell(row, 1).Value = "BIOZIN ROYALE — Reporte Financiero";
        var titleRange = ws.Range(row, 1, row, 7);
        titleRange.Merge().Style
            .Font.SetBold(true)
            .Font.SetFontSize(16)
            .Font.SetFontColor(goldBg)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
        row++;

        ws.Cell(row, 1).Value = titulo;
        ws.Range(row, 1, row, 7).Merge().Style.Font.SetFontSize(11).Font.SetFontColor(XLColor.FromHtml("#555555"));
        row++;

        ws.Cell(row, 1).Value = $"Período: {label}";
        ws.Range(row, 1, row, 7).Merge().Style.Font.SetFontSize(9).Font.SetFontColor(XLColor.FromHtml("#777777"));
        row++;

        ws.Cell(row, 1).Value = $"Generado: {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC";
        ws.Range(row, 1, row, 7).Merge().Style.Font.SetFontSize(8).Font.SetFontColor(XLColor.FromHtml("#AAAAAA"));
        row += 2;

        // Resumen
        ws.Cell(row, 1).Value = "RESUMEN DEL PERÍODO";
        ws.Range(row, 1, row, 3).Merge().Style
            .Font.SetBold(true).Font.SetFontSize(10).Font.SetFontColor(goldBg);
        row++;

        var depositos    = filas.Where(f => f.TransactionType == "deposit"   && f.Status == "completed").ToList();
        var retiros      = filas.Where(f => f.TransactionType == "withdrawal" && f.Status == "completed").ToList();
        var depTotal     = depositos.Sum(f => f.Amount);
        var retTotal     = retiros.Sum(f => f.Amount);
        var netProfit    = depTotal - retTotal;

        void SummaryRow(string concepto, int count, decimal total, XLColor? amtColor = null)
        {
            ws.Cell(row, 1).Value = concepto;
            ws.Cell(row, 2).Value = count;
            ws.Cell(row, 3).Value = total;
            ws.Cell(row, 3).Style.NumberFormat.Format = "$#,##0.00";
            if (amtColor is not null)
            {
                ws.Cell(row, 1).Style.Font.SetBold(true).Font.SetFontColor(amtColor);
                ws.Cell(row, 3).Style.Font.SetBold(true).Font.SetFontColor(amtColor);
            }
            ws.Range(row, 1, row, 3).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            ws.Range(row, 1, row, 3).Style.Border.BottomBorderColor = XLColor.FromHtml("#DDDDDD");
            row++;
        }

        SummaryRow("Depósitos completados",  depositos.Count, depTotal,  greenText);
        SummaryRow("Retiros completados",    retiros.Count,   retTotal,  redText);
        SummaryRow("Ganancia neta",          0,               netProfit, netProfit >= 0 ? greenText : redText);
        row++;

        // Tabla de transacciones
        ws.Cell(row, 1).Value = "DETALLE DE TRANSACCIONES";
        ws.Range(row, 1, row, 7).Merge().Style
            .Font.SetBold(true).Font.SetFontSize(10).Font.SetFontColor(goldBg);
        row++;

        // Headers
        string[] headers = { "Fecha", "Hora", "Usuario", "Nombre", "Tipo", "Monto", "Estado" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(row, c + 1);
            cell.Value = headers[c];
            cell.Style
                .Font.SetBold(true)
                .Font.SetFontColor(white)
                .Fill.SetBackgroundColor(headerBg)
                .Border.SetBottomBorder(XLBorderStyleValues.Medium)
                .Border.SetBottomBorderColor(goldBg)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }
        row++;

        // Filas de datos
        var localTz = TimeZoneInfo.Local;
        bool alt = false;
        foreach (var f in filas)
        {
            var dt       = TimeZoneInfo.ConvertTimeFromUtc(f.CreatedAt, localTz);
            var isDeposit = f.TransactionType == "deposit";
            var bg       = alt ? goldLight : white;

            ws.Cell(row, 1).Value = dt.ToString("dd/MM/yyyy");
            ws.Cell(row, 2).Value = dt.ToString("HH:mm");
            ws.Cell(row, 3).Value = f.Username;
            ws.Cell(row, 4).Value = f.DisplayName;
            ws.Cell(row, 5).Value = isDeposit ? "Depósito" : "Retiro";
            ws.Cell(row, 6).Value = isDeposit ? f.Amount : -f.Amount;
            ws.Cell(row, 6).Style.NumberFormat.Format = "$#,##0.00";
            ws.Cell(row, 6).Style.Font.SetFontColor(isDeposit ? greenText : redText);
            ws.Cell(row, 7).Value = StatusLabel(f.Status);

            ws.Range(row, 1, row, 7).Style.Fill.SetBackgroundColor(bg);
            ws.Range(row, 1, row, 7).Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
            ws.Range(row, 1, row, 7).Style.Border.SetBottomBorderColor(XLColor.FromHtml("#EEEEEE"));

            alt = !alt;
            row++;
        }

        // Ajustar ancho de columnas
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        resultado.ReturnValue = stream.ToArray();
        return Task.FromResult(resultado);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (DateTime from, DateTime to, string label, string titulo) GetRange(string period)
    {
        var hoy = DateTime.UtcNow.Date;
        switch (period)
        {
            case "w":
                var diasDesdeLunes = hoy.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)hoy.DayOfWeek - 1;
                var lunes = hoy.AddDays(-diasDesdeLunes);
                return (lunes, lunes.AddDays(7),
                    $"{lunes:dd/MM/yyyy} – {lunes.AddDays(6):dd/MM/yyyy}",
                    "Reporte Semanal");
            case "m":
                var inicioMes = new DateTime(hoy.Year, hoy.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                return (inicioMes, inicioMes.AddMonths(1),
                    hoy.ToString("MMMM yyyy"),
                    "Reporte Mensual");
            case "y":
                var inicioAnio = new DateTime(hoy.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return (inicioAnio, inicioAnio.AddYears(1),
                    hoy.Year.ToString(),
                    "Reporte Anual");
            default: // "d"
                return (hoy, hoy.AddDays(1),
                    hoy.ToString("dd/MM/yyyy"),
                    "Reporte Diario");
        }
    }

    private List<TFinanzasTransaccionResultado> GetFilas(DateTime from, DateTime to)
    {
        var txs = _unitOfWork.WalletTransactions
            .ObtenerEntidades(t =>
                (t.TransactionType == "deposit" || t.TransactionType == "withdrawal")
                && t.CreatedAt >= from && t.CreatedAt < to)
            .ReturnValue!
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        var walletIds = txs.Select(t => t.WalletId).Distinct().ToList();
        var walletMap = _unitOfWork.Wallets
            .ObtenerEntidades(w => walletIds.Contains(w.Id))
            .ReturnValue!
            .ToDictionary(w => w.Id, w => w.UserId);

        var userIds = walletMap.Values.Distinct().ToList();
        var profileMap = _unitOfWork.Profiles
            .ObtenerEntidades(p => userIds.Contains(p.UserId))
            .ReturnValue!
            .ToDictionary(p => p.UserId, p => p);

        return txs.Select(t =>
        {
            walletMap.TryGetValue(t.WalletId, out var uid);
            profileMap.TryGetValue(uid, out var perfil);
            return new TFinanzasTransaccionResultado
            {
                Id              = t.Id,
                TransactionType = t.TransactionType,
                Status          = t.Status,
                Amount          = t.Amount,
                CreatedAt       = t.CreatedAt,
                Username        = perfil?.Username    ?? "–",
                DisplayName     = perfil?.DisplayName ?? "–",
            };
        }).ToList();
    }

    private static string StatusLabel(string status) => status switch
    {
        "completed" => "Completado",
        "pending"   => "En proceso",
        "failed"    => "Fallido",
        _           => status,
    };

    // QuestPDF cell helpers
    private static void SummaryHeaderCell(TableDescriptor table, string text)
        => table.Cell()
            .Background("#1A1A2E").PaddingVertical(5).PaddingHorizontal(4)
            .Text(text).Bold().FontColor("#D4AF37");

    private static void SummaryDataCell(TableDescriptor table, string text, string? color = null)
    {
        var cell = table.Cell()
            .BorderBottom(1).BorderColor("#EEEEEE")
            .PaddingVertical(4).PaddingHorizontal(4);
        var t = cell.Text(text);
        if (color is not null) t.FontColor(color);
    }

    private static void TxHeaderCell(TableDescriptor table, string text)
        => table.Cell()
            .Background("#1A1A2E").PaddingVertical(5).PaddingHorizontal(3)
            .Text(text).Bold().FontSize(8).FontColor("#D4AF37");

    private static void TxDataCell(TableDescriptor table, string text, string? color = null)
    {
        var cell = table.Cell()
            .BorderBottom(1).BorderColor("#EEEEEE")
            .PaddingVertical(3).PaddingHorizontal(3);
        var t = cell.Text(text).FontSize(8);
        if (color is not null) t.FontColor(color);
    }
}
