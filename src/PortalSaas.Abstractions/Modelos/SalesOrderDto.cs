namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Orden de Venta (SAP ORDR/Orders) -- digitación directa, primer documento real que
/// habla con el SAP de una organización cliente (ver CLAUDE.md, alcance recortado de
/// esta primera entrega: solo líneas de Artículo, sin tabs Logística/Finanzas
/// todavía). Construido/mapeado a mano por ISalesOrderService, nunca vía
/// IHanaService.QueryAsync -- puede ser un record posicional normal (a diferencia de
/// los DTOs de catálogo, que si pasan por reflection, ver CustomerDto).
/// </summary>
public sealed record SalesOrderDto(
    string CustomerCardCode,
    string? CustomerName,
    int? SalesEmployeeCode,
    string? Comments,
    DateOnly DocDate,
    DateOnly DocDueDate,
    DateOnly TaxDate,
    string? CustomerReferenceNumber,
    IReadOnlyList<SalesOrderLineDto> Lines,
    int? DocEntry = null,
    int? DocNum = null,
    decimal? DocTotal = null,
    string? Status = null);

/// <summary>Línea de tipo Artículo -- sin soporte de línea de Servicio en esta primera entrega.</summary>
public sealed record SalesOrderLineDto(
    string ItemCode,
    string? Description,
    decimal Quantity,
    decimal? UnitPrice,
    decimal DiscountPercent,
    string WarehouseCode);

public sealed record SalesOrderFilter(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    string? CustomerCardCode = null,
    string? CustomerName = null,
    int? DocNum = null);

public sealed record SalesOrderListResult(IReadOnlyList<SalesOrderSummaryDto> Items, int TotalRecords);

/// <summary>Fila de listado -- non-positional, se mapea vía IHanaService.QueryAsync (ver CustomerDto).</summary>
public sealed record SalesOrderSummaryDto
{
    public int DocEntry { get; init; }
    public int DocNum { get; init; }
    public string CustomerCardCode { get; init; } = null!;
    public string? CustomerName { get; init; }
    public DateTime DocDate { get; init; }
    public decimal DocTotal { get; init; }
    public string? CustomerReferenceNumber { get; init; }
    public string? SalesEmployeeName { get; init; }
    public string Status { get; init; } = null!;
}
