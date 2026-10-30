namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Fila del reporte de cierre: una por línea de gasto de una rendición aprobada en el
/// período. Pensada para exportar (gasto, centro de costo, tipo de gasto) y cargar
/// manualmente a SAP B1.
/// </summary>
public sealed record ClosingReportLine(
    long ExpenseReportId,
    int Round,
    string EmployeeName,
    string? CostCenterCode,
    string? CostCenterName,
    string ExpenseTypeName,
    DateTimeOffset Date,
    decimal Amount,
    string Currency,
    string? Notes,
    /// <summary>
    /// Monto efectivamente reembolsado/contabilizado (ExpenseReportLine.ReimbursementAmount,
    /// Task 7) -- puede ser menor que <see cref="Amount"/> cuando una política de gasto topó
    /// el reembolso. Es lo que se exporta/contabiliza en SAP; Amount queda como referencia de
    /// lo que decía la boleta.
    /// </summary>
    decimal ReimbursementAmount);
