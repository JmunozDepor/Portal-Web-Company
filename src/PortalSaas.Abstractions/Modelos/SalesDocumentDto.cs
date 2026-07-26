namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Documento de venta genérico (SAP ORDR/ORIN/OINV/ORRR/ORDN, ver SalesDocumentType) --
/// digitación directa, generaliza el antiguo SalesOrderDto (una Orden de Venta es hoy
/// solo el primer valor de SalesDocumentType, ver CLAUDE.md). Construido/mapeado a mano
/// por ISalesDocumentService, nunca vía IHanaService.QueryAsync -- puede ser un record
/// posicional normal (a diferencia de los DTOs de catálogo, que sí pasan por reflection,
/// ver CustomerDto).
/// </summary>
public sealed record SalesDocumentDto(
    string CustomerCardCode,
    string? CustomerName,
    int? SalesEmployeeCode,
    string? Comments,
    DateOnly DocDate,
    DateOnly DocDueDate,
    DateOnly TaxDate,
    string? CustomerReferenceNumber,
    IReadOnlyList<SalesDocumentLineDto> Lines,
    int? DocEntry = null,
    int? DocNum = null,
    decimal? DocTotal = null,
    string? Status = null);

/// <summary>Línea de tipo Artículo -- sin soporte de línea de Servicio todavía (ver CLAUDE.md).</summary>
public sealed record SalesDocumentLineDto(
    string ItemCode,
    string? Description,
    decimal Quantity,
    decimal? UnitPrice,
    decimal DiscountPercent,
    string WarehouseCode);

public sealed record SalesDocumentFilter(
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    string? CustomerCardCode = null,
    string? CustomerName = null,
    int? DocNum = null);

public sealed record SalesDocumentListResult(IReadOnlyList<SalesDocumentSummaryDto> Items, int TotalRecords);

/// <summary>Fila de listado -- non-positional, se mapea vía IHanaService.QueryAsync (ver CustomerDto).</summary>
public sealed record SalesDocumentSummaryDto
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
