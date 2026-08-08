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
        var hoy    = DateTime.UtcNow.Date;
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

        var depositos  = filas.Where(f => f.TransactionType == "deposit"   && f.Status == "completed").ToList();
        var retiros    = filas.Where(f => f.TransactionType == "withdrawal" && f.Status == "completed").ToList();
        var depTotal   = depositos.Sum(f => f.Amount);
        var retTotal   = retiros.Sum(f => f.Amount);
        var netProfit  = depTotal - retTotal;

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(9).FontColor("#1A1A1A"));

                // ── Header ────────────────────────────────────────────────────
                page.Header().Column(col =>
                {
                    // Navy band
                    col.Item()
                        .Background("#0D0B18")
                        .PaddingHorizontal(28).PaddingVertical(18)
                        .Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("BIOZIN ROYALE")
                                    .Bold().FontSize(20).FontColor("#D4A73C");
                                c.Item().PaddingTop(3)
                                    .Text("Plataforma de juegos en línea")
                                    .FontSize(8).FontColor("#6A6480");
                            });
                            row.RelativeItem().AlignRight().Column(c =>
                            {
                                c.Item().Text(titulo.ToUpperInvariant())
                                    .Bold().FontSize(12).FontColor("#FFFFFF");
                                c.Item().PaddingTop(3)
                                    .Text($"Período: {label}")
                                    .FontSize(8.5f).FontColor("#7A7490");
                                c.Item().PaddingTop(2)
                                    .Text($"Generado: {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC")
                                    .FontSize(8).FontColor("#4A4460");
                            });
                        });

                    // Gold accent line
                    col.Item().Height(3).Background("#D4A73C");
                    col.Item().Height(10);
                });

                // ── Content ───────────────────────────────────────────────────
                page.Content().PaddingHorizontal(28).Column(col =>
                {
                    // Section title — resumen
                    col.Item().PaddingBottom(8).Row(row =>
                    {
                        row.ConstantItem(3).Background("#D4A73C");
                        row.RelativeItem().PaddingLeft(8).PaddingVertical(3)
                            .Text("RESUMEN DEL PERÍODO")
                            .Bold().FontSize(8.5f).FontColor("#0D0B18");
                    });

                    // KPI Cards
                    col.Item().PaddingBottom(16).Row(row =>
                    {
                        PdfKpiCard(row, "DEPÓSITOS",    depTotal,  depositos.Count, "#2E7A4E", "#4A9E6A");
                        row.ConstantItem(8);
                        PdfKpiCard(row, "RETIROS",      retTotal,  retiros.Count,   "#A83838", "#C95858");
                        row.ConstantItem(8);
                        PdfKpiCard(row, "GANANCIA NETA", netProfit, 0,
                            netProfit >= 0 ? "#B8860B" : "#A83838",
                            netProfit >= 0 ? "#D4A73C" : "#C95858");
                    });

                    // Section title — detalle
                    col.Item().PaddingBottom(8).Row(row =>
                    {
                        row.ConstantItem(3).Background("#D4A73C");
                        row.RelativeItem().PaddingLeft(8).PaddingVertical(3)
                            .Text("DETALLE DE TRANSACCIONES")
                            .Bold().FontSize(8.5f).FontColor("#0D0B18");
                    });

                    if (filas.Count == 0)
                    {
                        col.Item().PaddingVertical(14)
                            .Text("No hay transacciones en este período.")
                            .FontColor("#999999").Italic();
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(1.8f); // Fecha
                                cols.RelativeColumn(1.2f); // Hora
                                cols.RelativeColumn(1.8f); // Usuario
                                cols.RelativeColumn(2.0f); // Nombre
                                cols.RelativeColumn(1.5f); // Tipo
                                cols.RelativeColumn(1.5f); // Monto
                                cols.RelativeColumn(1.5f); // Estado
                            });

                            foreach (var h in new[] { "Fecha", "Hora", "Usuario", "Nombre", "Tipo", "Monto", "Estado" })
                                PdfTxHeader(table, h);

                            var localTz = TimeZoneInfo.Local;
                            bool alt    = false;
                            foreach (var f in filas)
                            {
                                var dt        = TimeZoneInfo.ConvertTimeFromUtc(f.CreatedAt, localTz);
                                var isDeposit = f.TransactionType == "deposit";
                                var rowBg     = alt ? "#FAFAF6" : "#FFFFFF";

                                PdfTxCell(table, dt.ToString("dd/MM/yyyy"), rowBg, center: true);
                                PdfTxCell(table, dt.ToString("HH:mm"),     rowBg, center: true);
                                PdfTxCell(table, f.Username,   rowBg);
                                PdfTxCell(table, f.DisplayName, rowBg);

                                // Tipo — colored background
                                table.Cell()
                                    .Background(isDeposit ? "#E6F4EC" : "#FAE8E8")
                                    .BorderBottom(1).BorderColor("#EEEEEE")
                                    .PaddingVertical(4).PaddingHorizontal(5)
                                    .AlignCenter()
                                    .Text(isDeposit ? "Depósito" : "Retiro")
                                    .FontSize(7.5f).FontColor(isDeposit ? "#2E7A4E" : "#A83838").Bold();

                                // Monto
                                table.Cell()
                                    .Background(rowBg)
                                    .BorderBottom(1).BorderColor("#EEEEEE")
                                    .PaddingVertical(4).PaddingHorizontal(5)
                                    .AlignRight()
                                    .Text($"{(isDeposit ? "+" : "-")}${f.Amount:N2}")
                                    .FontSize(7.5f).FontColor(isDeposit ? "#2E7A4E" : "#A83838").Bold();

                                // Estado — colored background
                                var (statusBg, statusColor) = f.Status switch
                                {
                                    "completed" => ("#EAF0FF", "#2850A0"),
                                    "pending"   => ("#FFF5E0", "#9A6800"),
                                    "failed"    => ("#FAE8E8", "#A83838"),
                                    _           => ("#F0F0F0", "#666666"),
                                };
                                table.Cell()
                                    .Background(statusBg)
                                    .BorderBottom(1).BorderColor("#EEEEEE")
                                    .PaddingVertical(4).PaddingHorizontal(5)
                                    .AlignCenter()
                                    .Text(StatusLabel(f.Status))
                                    .FontSize(7.5f).FontColor(statusColor).Bold();

                                alt = !alt;
                            }
                        });
                    }

                    col.Item().Height(16);
                });

                // ── Footer ────────────────────────────────────────────────────
                page.Footer().PaddingHorizontal(28).Column(col =>
                {
                    col.Item()
                        .BorderTop(1).BorderColor("#D4A73C")
                        .PaddingTop(8)
                        .Row(row =>
                        {
                            row.RelativeItem()
                                .Text("Biozin Royale · Reporte Confidencial")
                                .FontSize(8).FontColor("#AAAAAA");
                            row.AutoItem().Text(t =>
                            {
                                t.Span("Página ").FontSize(8).FontColor("#AAAAAA");
                                t.CurrentPageNumber().FontSize(8).FontColor("#AAAAAA");
                                t.Span(" de ").FontSize(8).FontColor("#AAAAAA");
                                t.TotalPages().FontSize(8).FontColor("#AAAAAA");
                            });
                        });
                    col.Item().Height(14);
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

        var depositos  = filas.Where(f => f.TransactionType == "deposit"   && f.Status == "completed").ToList();
        var retiros    = filas.Where(f => f.TransactionType == "withdrawal" && f.Status == "completed").ToList();
        var depTotal   = depositos.Sum(f => f.Amount);
        var retTotal   = retiros.Sum(f => f.Amount);
        var netProfit  = depTotal - retTotal;

        // Paleta
        var navy      = XLColor.FromHtml("#0D0B18");
        var gold      = XLColor.FromHtml("#D4A73C");
        var white     = XLColor.White;
        var greenBg   = XLColor.FromHtml("#E6F4EC");
        var greenTxt  = XLColor.FromHtml("#2E7A4E");
        var redBg     = XLColor.FromHtml("#FAE8E8");
        var redTxt    = XLColor.FromHtml("#A83838");
        var goldBg    = XLColor.FromHtml("#FFFAED");
        var goldTxt   = XLColor.FromHtml("#B8860B");
        var blueBg    = XLColor.FromHtml("#EAF0FF");
        var blueTxt   = XLColor.FromHtml("#2850A0");
        var yellowBg  = XLColor.FromHtml("#FFF5E0");
        var yellowTxt = XLColor.FromHtml("#9A6800");
        var creamBg   = XLColor.FromHtml("#F5F2EC");
        var altBg     = XLColor.FromHtml("#FAFAF6");
        var muted     = XLColor.FromHtml("#AAAAAA");

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Reporte Financiero");

        int r = 1;

        // ── Título (filas 1-4, fondo navy) ───────────────────────────────────
        void NavyRow(int rowIdx, string text, double fontSize, XLColor color)
        {
            var range = ws.Range(rowIdx, 1, rowIdx, 7);
            range.Merge();
            range.Style.Fill.BackgroundColor          = navy;
            range.Style.Font.FontColor                = color;
            range.Style.Font.FontSize                 = fontSize;
            range.Style.Alignment.Horizontal          = XLAlignmentHorizontalValues.Left;
            range.Style.Alignment.Vertical            = XLAlignmentVerticalValues.Center;
            ws.Cell(rowIdx, 1).Value = text;
        }

        ws.Row(r).Height = 28;
        NavyRow(r, "BIOZIN ROYALE — Reporte Financiero", 14, gold);
        ws.Cell(r, 1).Style.Font.Bold = true;
        r++;

        ws.Row(r).Height = 18;
        NavyRow(r, titulo, 11, XLColor.White);
        r++;

        ws.Row(r).Height = 16;
        NavyRow(r, $"Período: {label}", 9, XLColor.FromHtml("#7A7490"));
        r++;

        ws.Row(r).Height = 14;
        NavyRow(r, $"Generado: {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC", 8, XLColor.FromHtml("#4A4460"));
        r++;

        // Espaciador
        ws.Range(r, 1, r, 7).Style.Fill.BackgroundColor = navy;
        ws.Row(r).Height = 6;
        r++;

        // ── Sección: Resumen ──────────────────────────────────────────────────
        XlSectionLabel(ws, r, "RESUMEN DEL PERÍODO", gold);
        ws.Row(r).Height = 18;
        r++;

        // KPI headers (A-B = Depósitos, C-D = Retiros, E-G = Ganancia Neta)
        void KpiHeader(int col1, int col2, string text)
        {
            var range = ws.Range(r, col1, r, col2);
            range.Merge();
            range.Style.Fill.BackgroundColor = navy;
            range.Style.Font.FontColor       = gold;
            range.Style.Font.Bold            = true;
            range.Style.Font.FontSize        = 8;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            range.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            range.Style.Border.OutsideBorder      = XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = XLColor.FromHtml("#1A1830");
            ws.Cell(r, col1).Value = text;
        }
        KpiHeader(1, 2, "DEPÓSITOS");
        KpiHeader(3, 4, "RETIROS");
        KpiHeader(5, 7, "GANANCIA NETA");
        ws.Row(r).Height = 18;
        r++;

        // KPI values
        void KpiValue(int col1, int col2, decimal amount, XLColor bg, XLColor fg)
        {
            var range = ws.Range(r, col1, r, col2);
            range.Merge();
            range.Style.Fill.BackgroundColor = bg;
            range.Style.Font.FontColor       = fg;
            range.Style.Font.Bold            = true;
            range.Style.Font.FontSize        = 14;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            range.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            range.Style.Border.OutsideBorder      = XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = XLColor.FromHtml("#E8E8E8");
            ws.Cell(r, col1).Value = amount;
            ws.Cell(r, col1).Style.NumberFormat.Format = "$#,##0.00";
        }
        KpiValue(1, 2, depTotal,  greenBg, greenTxt);
        KpiValue(3, 4, retTotal,  redBg,   redTxt);
        KpiValue(5, 7, netProfit, goldBg,  netProfit >= 0 ? goldTxt : redTxt);
        ws.Row(r).Height = 26;
        r++;

        // KPI subtitles (count)
        void KpiSub(int col1, int col2, string text)
        {
            var range = ws.Range(r, col1, r, col2);
            range.Merge();
            range.Style.Font.FontColor       = muted;
            range.Style.Font.FontSize        = 8;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            range.Style.Fill.BackgroundColor = white;
            range.Style.Border.OutsideBorder      = XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = XLColor.FromHtml("#E8E8E8");
            ws.Cell(r, col1).Value = text;
        }
        KpiSub(1, 2, $"{depositos.Count} transacciones");
        KpiSub(3, 4, $"{retiros.Count} transacciones");
        KpiSub(5, 7, string.Empty);
        ws.Row(r).Height = 14;
        r++;

        // Espaciador
        r++;

        // ── Sección: Detalle de transacciones ────────────────────────────────
        XlSectionLabel(ws, r, "DETALLE DE TRANSACCIONES", gold);
        ws.Row(r).Height = 18;
        r++;

        // Table headers
        string[] headers = { "Fecha", "Hora", "Usuario", "Nombre", "Tipo", "Monto", "Estado" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(r, c + 1);
            cell.Value = headers[c];
            cell.Style.Fill.BackgroundColor = navy;
            cell.Style.Font.FontColor       = gold;
            cell.Style.Font.Bold            = true;
            cell.Style.Font.FontSize        = 8;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            cell.Style.Border.BottomBorder      = XLBorderStyleValues.Medium;
            cell.Style.Border.BottomBorderColor = gold;
        }
        ws.Row(r).Height = 20;
        r++;

        // Data rows
        var localTz = TimeZoneInfo.Local;
        bool altRow = false;
        int  txStart = r;
        foreach (var f in filas)
        {
            var dt        = TimeZoneInfo.ConvertTimeFromUtc(f.CreatedAt, localTz);
            var isDeposit = f.TransactionType == "deposit";
            var rowBg     = altRow ? altBg : white;

            void SetBase(IXLCell cell)
            {
                cell.Style.Fill.BackgroundColor    = rowBg;
                cell.Style.Font.FontSize           = 9.5;
                cell.Style.Border.BottomBorder     = XLBorderStyleValues.Thin;
                cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#E8E8E8");
                cell.Style.Alignment.Vertical      = XLAlignmentVerticalValues.Center;
            }

            var c1 = ws.Cell(r, 1); SetBase(c1); c1.Value = dt.ToString("dd/MM/yyyy");
            c1.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var c2 = ws.Cell(r, 2); SetBase(c2); c2.Value = dt.ToString("HH:mm");
            c2.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var c3 = ws.Cell(r, 3); SetBase(c3); c3.Value = f.Username;
            var c4 = ws.Cell(r, 4); SetBase(c4); c4.Value = f.DisplayName;

            // Tipo — colored cell
            var c5 = ws.Cell(r, 5);
            c5.Value = isDeposit ? "Depósito" : "Retiro";
            c5.Style.Fill.BackgroundColor    = isDeposit ? greenBg : redBg;
            c5.Style.Font.FontColor          = isDeposit ? greenTxt : redTxt;
            c5.Style.Font.Bold               = true;
            c5.Style.Font.FontSize           = 9;
            c5.Style.Alignment.Horizontal    = XLAlignmentHorizontalValues.Center;
            c5.Style.Alignment.Vertical      = XLAlignmentVerticalValues.Center;
            c5.Style.Border.BottomBorder     = XLBorderStyleValues.Thin;
            c5.Style.Border.BottomBorderColor = XLColor.FromHtml("#E8E8E8");

            // Monto
            var c6 = ws.Cell(r, 6);
            c6.Value = isDeposit ? f.Amount : -f.Amount;
            c6.Style.NumberFormat.Format      = "$#,##0.00";
            c6.Style.Font.FontColor           = isDeposit ? greenTxt : redTxt;
            c6.Style.Font.Bold                = true;
            c6.Style.Font.FontSize            = 9.5;
            c6.Style.Fill.BackgroundColor     = rowBg;
            c6.Style.Alignment.Horizontal     = XLAlignmentHorizontalValues.Right;
            c6.Style.Alignment.Vertical       = XLAlignmentVerticalValues.Center;
            c6.Style.Border.BottomBorder      = XLBorderStyleValues.Thin;
            c6.Style.Border.BottomBorderColor = XLColor.FromHtml("#E8E8E8");

            // Estado — colored cell
            var c7 = ws.Cell(r, 7);
            c7.Value = StatusLabel(f.Status);
            var (sBg, sFg) = f.Status switch
            {
                "completed" => (blueBg,   blueTxt),
                "pending"   => (yellowBg, yellowTxt),
                "failed"    => (redBg,    redTxt),
                _           => (creamBg,  XLColor.FromHtml("#666666")),
            };
            c7.Style.Fill.BackgroundColor     = sBg;
            c7.Style.Font.FontColor           = sFg;
            c7.Style.Font.Bold                = true;
            c7.Style.Font.FontSize            = 9;
            c7.Style.Alignment.Horizontal     = XLAlignmentHorizontalValues.Center;
            c7.Style.Alignment.Vertical       = XLAlignmentVerticalValues.Center;
            c7.Style.Border.BottomBorder      = XLBorderStyleValues.Thin;
            c7.Style.Border.BottomBorderColor = XLColor.FromHtml("#E8E8E8");

            ws.Row(r).Height = 17;
            altRow = !altRow;
            r++;
        }

        // Totals row
        if (filas.Count > 0)
        {
            var totalRange = ws.Range(r, 1, r, 7);
            totalRange.Style.Fill.BackgroundColor = creamBg;
            totalRange.Style.Border.TopBorder      = XLBorderStyleValues.Medium;
            totalRange.Style.Border.TopBorderColor  = gold;

            ws.Range(r, 1, r, 4).Merge();
            var ct1 = ws.Cell(r, 1);
            ct1.Value = "TOTAL DEL PERÍODO";
            ct1.Style.Font.Bold               = true;
            ct1.Style.Font.FontSize           = 9;
            ct1.Style.Font.FontColor          = XLColor.FromHtml("#0D0B18");
            ct1.Style.Fill.BackgroundColor    = creamBg;
            ct1.Style.Alignment.Vertical      = XLAlignmentVerticalValues.Center;

            ws.Cell(r, 5).Style.Fill.BackgroundColor = creamBg;

            var ct6 = ws.Cell(r, 6);
            ct6.Value = depTotal - retTotal;
            ct6.Style.NumberFormat.Format      = "$#,##0.00";
            ct6.Style.Font.Bold               = true;
            ct6.Style.Font.FontSize           = 11;
            ct6.Style.Font.FontColor          = netProfit >= 0 ? goldTxt : redTxt;
            ct6.Style.Fill.BackgroundColor    = creamBg;
            ct6.Style.Alignment.Horizontal    = XLAlignmentHorizontalValues.Right;
            ct6.Style.Alignment.Vertical      = XLAlignmentVerticalValues.Center;

            ws.Cell(r, 7).Style.Fill.BackgroundColor = creamBg;
            ws.Row(r).Height = 20;
        }

        // Freeze header + table headers, auto-fit columns
        ws.SheetView.FreezeRows(txStart - 1);
        ws.Columns().AdjustToContents();
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 12);
        ws.Column(2).Width = Math.Max(ws.Column(2).Width, 8);

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        resultado.ReturnValue = stream.ToArray();
        return Task.FromResult(resultado);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (DateTime from, DateTime to, string label, string titulo) GetRange(string period)
    {
        var hoy = DateTime.UtcNow.Date;
        return period switch
        {
            "w" => (
                hoy.AddDays(-(hoy.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)hoy.DayOfWeek - 1)),
                hoy.AddDays(-(hoy.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)hoy.DayOfWeek - 1)).AddDays(7),
                $"{hoy.AddDays(-(hoy.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)hoy.DayOfWeek - 1)):dd/MM/yyyy} – {hoy.AddDays(-(hoy.DayOfWeek == DayOfWeek.Sunday ? 6 : (int)hoy.DayOfWeek - 1)).AddDays(6):dd/MM/yyyy}",
                "Reporte Semanal"),
            "m" => (
                new DateTime(hoy.Year, hoy.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(hoy.Year, hoy.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
                hoy.ToString("MMMM yyyy"),
                "Reporte Mensual"),
            "y" => (
                new DateTime(hoy.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(hoy.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddYears(1),
                hoy.Year.ToString(),
                "Reporte Anual"),
            _ => (hoy, hoy.AddDays(1), hoy.ToString("dd/MM/yyyy"), "Reporte Diario"),
        };
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

        var walletIds  = txs.Select(t => t.WalletId).Distinct().ToList();
        var walletMap  = _unitOfWork.Wallets
            .ObtenerEntidades(w => walletIds.Contains(w.Id))
            .ReturnValue!
            .ToDictionary(w => w.Id, w => w.UserId);

        var userIds    = walletMap.Values.Distinct().ToList();
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

    // ── PDF helpers ───────────────────────────────────────────────────────────

    private static void PdfKpiCard(
        RowDescriptor row, string label, decimal amount,
        int count, string amountColor, string accentColor)
    {
        row.RelativeItem().Row(r =>
        {
            r.ConstantItem(4).Background(accentColor);
            r.RelativeItem()
                .Border(1).BorderColor("#E8E4DC")
                .Padding(10)
                .Column(c =>
                {
                    c.Item().Text(label)
                        .FontSize(7.5f).FontColor("#888080").Bold();
                    c.Item().PaddingTop(4)
                        .Text($"${amount:N2}")
                        .FontSize(16).FontColor(amountColor).Bold();
                    c.Item().PaddingTop(3)
                        .Text(count > 0 ? $"{count} transacciones" : " ")
                        .FontSize(8).FontColor("#AAAAAA");
                });
        });
    }

    private static void PdfTxHeader(TableDescriptor table, string text)
        => table.Cell()
            .Background("#0D0B18")
            .BorderBottom(2).BorderColor("#D4A73C")
            .PaddingVertical(6).PaddingHorizontal(4)
            .AlignCenter()
            .Text(text).Bold().FontSize(7).FontColor("#D4A73C");

    private static void PdfTxCell(
        TableDescriptor table, string text, string bg,
        string? color = null, bool center = false)
    {
        var cell = table.Cell()
            .Background(bg)
            .BorderBottom(1).BorderColor("#EEEEEE")
            .PaddingVertical(4).PaddingHorizontal(5);

        if (center) cell = cell.AlignCenter();

        var t = cell.Text(text).FontSize(7.5f);
        if (color is not null) t.FontColor(color);
    }

    // ── Excel helpers ─────────────────────────────────────────────────────────

    private static void XlSectionLabel(IXLWorksheet ws, int row, string text, XLColor borderColor)
    {
        var range = ws.Range(row, 1, row, 7);
        range.Merge();
        range.Style.Fill.BackgroundColor      = XLColor.FromHtml("#F5F2EC");
        range.Style.Font.Bold                 = true;
        range.Style.Font.FontSize             = 8.5;
        range.Style.Font.FontColor            = XLColor.FromHtml("#0D0B18");
        range.Style.Alignment.Vertical        = XLAlignmentVerticalValues.Center;
        range.Style.Border.LeftBorder         = XLBorderStyleValues.Thick;
        range.Style.Border.LeftBorderColor    = borderColor;
        range.Style.Border.BottomBorder       = XLBorderStyleValues.Thin;
        range.Style.Border.BottomBorderColor  = XLColor.FromHtml("#D0D0D0");
        ws.Cell(row, 1).Value = "  " + text;
    }
}
