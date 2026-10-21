namespace Modulo.Despachos.Servicios;

public sealed record DailyDetailRow(string ShiftName, string CarrierName, decimal Cost, decimal Units, int Documents, int Packages);

public sealed record DailyRow(
    DateOnly Date, decimal Cost, decimal Units, int Documents, int Packages, IReadOnlyList<DailyDetailRow> Details);

/// <summary>
/// Reporte mensual por dispatch_date, sin manifiestos anulados. El costo es cost_final (null cuenta como 0 y se
/// informa en ManifestsWithoutCost); unidades, documentos y bultos salen del snapshot del manifiesto.
/// </summary>
public sealed record MonthlyDispatchReport(
    decimal TotalCost, decimal TotalUnits, int TotalDocuments, int TotalPackages, IReadOnlyList<DailyRow> Days,
    int ManifestsWithoutCost = 0);

public interface IDispatchReportService
{
    Task<MonthlyDispatchReport> GetMonthlyAsync(int year, int month);

    /// <summary>Excel (ClosedXML), hoja "Resumen" con los KPIs y la tabla día / turno / transportista con totales.</summary>
    Task<byte[]> ExportMonthlyExcelAsync(int year, int month);
}
