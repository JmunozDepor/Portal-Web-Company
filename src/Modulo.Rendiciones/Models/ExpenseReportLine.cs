namespace Modulo.Rendiciones.Models;

/// <summary>
/// Un gasto. Se captura SUELTO (Status="Loose", con su propio dueño) y después un
/// informe (ExpenseReport) lo agrupa (Status="InReport") para enviarlo a aprobación --
/// gastos sueltos primero, informes después. Mientras está suelto, ExpenseReportId es
/// null. Portado de RendicionGastoDetalle (PortalSAP_v2).
/// </summary>
public class ExpenseReportLine
{
    public long Id { get; set; }

    /// <summary>Dueño del gasto. Obligatorio para toda fila nueva.</summary>
    public required Guid CompanyId { get; set; }

    public required Guid UserId { get; set; }

    /// <summary>Loose | InReport. No es el estado de aprobación (ese vive en ExpenseReport.Status).</summary>
    public string Status { get; set; } = "Loose";

    public long? ExpenseReportId { get; set; }

    public ExpenseReport? ExpenseReport { get; set; }

    /// <summary>Null mientras no se categoriza (típico de un gasto recién importado por OCR) -- debe estar seteado antes de entrar a un informe.</summary>
    public long? ExpenseTypeId { get; set; }

    public ExpenseType? ExpenseType { get; set; }

    /// <summary>Tipo de documento tributario (Boleta, Factura, etc.) -- identifica el IVA aplicable.</summary>
    public long? DocumentTypeId { get; set; }

    public DocumentType? DocumentType { get; set; }

    public DateTimeOffset Date { get; set; }

    /// <summary>Monto total (bruto, impuesto incluido).</summary>
    public decimal Amount { get; set; }

    /// <summary>Porción de Amount que corresponde a impuesto. Editable -- se sugiere desde DocumentType.TaxPercentage pero no se fuerza.</summary>
    public decimal? TaxAmount { get; set; }

    public required string Currency { get; set; }

    public string? DocumentNumber { get; set; }

    public string? SupplierTaxId { get; set; }

    public string? SupplierName { get; set; }

    public string? Notes { get; set; }

    public long? ExpenseReceiptId { get; set; }

    public ExpenseReceipt? ExpenseReceipt { get; set; }

    /// <summary>Solo si ExpenseType.IsMileage -- dirección de origen en texto libre, se geocodifica en la fase de servicios.</summary>
    public string? Origin { get; set; }

    public string? Destination { get; set; }

    /// <summary>Calculado al guardar -- nunca lo escribe el usuario a mano.</summary>
    public decimal? DistanceKm { get; set; }

    /// <summary>Tarifa vigente en ExpenseType.RatePerKm al momento de guardar, copiada acá para que un cambio de tarifa futuro no altere gastos ya guardados.</summary>
    public decimal? AppliedRatePerKm { get; set; }
}
