namespace PortalSaas.Abstractions.Modelos.Dte;

// Modelo canónico de un DTE del SII (formato 2.5), independiente del proveedor de documentos
// electrónicos (FACELE, BES, ...). Los módulos arman este modelo; cada adaptador lo traduce.
// Ver Portal SaaS - Core/docs/superpowers/specs/2026-10-22-pasarela-documentos-electronicos-design.md §5.1.

/// <summary>Documento tributario electrónico. <see cref="Totals"/> null = la plataforma los calcula desde las líneas.</summary>
public sealed record DteDocument(
    DteIdDoc IdDoc,
    DteIssuerOverride? Issuer,
    DteReceiver Receiver,
    DteTransport? Transport,
    DteTotals? Totals,
    IReadOnlyList<DteLine> Lines,
    IReadOnlyList<DteReference> References,
    IReadOnlyList<DteExtraField> Extras);

/// <summary>Identificación: tipo DTE, fecha, IndTraslado (TransferIndicator) y TipoDespacho (DispatchType).</summary>
public sealed record DteIdDoc(int DteType, DateOnly IssueDate, int? TransferIndicator, int? DispatchType);

/// <summary>
/// Solo sobrescribe DirOrigen / CmnaOrigen / CiudadOrigen. RUT, razón social, giro y Acteco del emisor
/// salen siempre de la configuración DTE de la compañía, nunca del módulo.
/// </summary>
public sealed record DteIssuerOverride(string? OriginAddress, string? OriginCommune, string? OriginCity);

public sealed record DteReceiver(string TaxId, string Name, string? Activity, string? Address, string? Commune, string? City);

/// <summary>Bloque Transporte (Res. Ex. SII N°154). No existe hora de llegada en el formato.</summary>
public sealed record DteTransport(
    string? Plate,
    string? TrailerPlate,
    string? CarrierTaxId,
    DteDriver? Driver,
    string? DestinationAddress,
    string? DestinationCommune,
    string? DestinationCity,
    DteCustoms? Customs,
    DateOnly? DepartureDate,
    TimeOnly? DepartureTime,
    DateOnly? ArrivalDate);

public sealed record DteDriver(string TaxId, string Name);

/// <summary>Transporte/Aduana: peso neto, bultos y nombre del transportista.</summary>
public sealed record DteCustoms(decimal? NetWeightKg, int? TotalPackages, string? CarrierName);

public sealed record DteTotals(decimal Net, decimal Exempt, decimal VatRate, decimal Vat, decimal Total);

public sealed record DteLine(
    int LineNumber, string? ItemCode, string Name, string? Description, decimal Quantity, string? Unit,
    decimal? UnitPrice, decimal Amount);

/// <summary>Referencia. DocumentType es texto porque admite "33", "801", "HES", etc.</summary>
public sealed record DteReference(int LineNumber, string DocumentType, long? Folio, DateOnly Date, int? ReasonCode, string? Reason);

/// <summary>Dato libre para el PDF (ej. N° de manifiesto); cada adaptador lo mapea si su proveedor lo soporta.</summary>
public sealed record DteExtraField(string Key, string Value);
