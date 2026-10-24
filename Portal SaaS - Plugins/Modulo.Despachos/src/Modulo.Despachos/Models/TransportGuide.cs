namespace Modulo.Despachos.Models;

/// <summary>
/// Guía de despacho electrónica (DTE 52, Res. Ex. SII N°154). Índice de TODAS las guías del módulo
/// (<see cref="Source"/>): las del manifiesto (una por destino, ver abajo) y las manuales (borrador GM-, sin documento
/// SAP, que viajan en un manifiesto como documento o se emiten solas). El detalle completo y el XML viven una sola
/// vez en dte_emissions de la plataforma.
/// Guía del manifiesto: planificada para un manifiesto, una por destino
/// (RUT receptor + dirección + comuna), dividida si excede los límites del formato. Se planifica una sola vez,
/// en el primer intento de despacho, y los reintentos reusan la misma <see cref="EmissionKey"/> (idempotencia en
/// la pasarela DTE de la plataforma, que guarda folio, XML y estado con el proveedor en dte_emissions).
/// </summary>
public class TransportGuide
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    /// <summary>Manifiesto que la planificó (solo Source = manifest). Una guía manual viaja en el manifiesto como documento.</summary>
    public Guid? ManifestId { get; set; }

    /// <summary>Orden dentro del manifiesto (1..N), solo Source = manifest. Índice único (manifest_id, sequence).</summary>
    public int? Sequence { get; set; }

    /// <summary>TransportGuideSources: manifest (Res. 154 desde documentos) o manual.</summary>
    public string Source { get; set; } = TransportGuideSources.Manifest;

    /// <summary>Correlativo del borrador manual (GM-000123); único por compañía.</summary>
    public int? Number { get; set; }

    /// <summary>Fecha de emisión pedida en el borrador manual.</summary>
    public DateOnly? IssueDate { get; set; }

    /// <summary>SII TipoDespacho de la guía manual (1 receptor, 2 emisor a cliente, 3 emisor a otras instalaciones).</summary>
    public int? DispatchType { get; set; }

    public long? OriginId { get; set; }

    /// <summary>Socio de negocio SAP del receptor; null = receptor digitado solo para esta guía.</summary>
    public string? ReceiverCardCode { get; set; }

    /// <summary>ManualTransportModes: del manifiesto, de los maestros o particular digitado.</summary>
    public string? TransportMode { get; set; }

    public decimal? NetAmount { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? TotalAmount { get; set; }

    /// <summary>Observaciones (glosa) de la guía; van como última línea del detalle.</summary>
    public string? Notes { get; set; }

    public string? AnnulReason { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? IssuedBy { get; set; }

    /// <summary>Clave estable de la emisión en la pasarela DTE (= uuid anti-duplicado del proveedor).</summary>
    public Guid EmissionKey { get; set; }

    public string ReceiverTaxId { get; set; } = "";
    public string ReceiverName { get; set; } = "";
    public string DestinationAddress { get; set; } = "";
    public string DestinationCommune { get; set; } = "";
    public string? DestinationCity { get; set; }
    public int TransferIndicator { get; set; }
    public int LineCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal? WeightKg { get; set; }

    /// <summary>
    /// DteDocument planificado (JSON). Lo que se reenvía en cada reintento: una guía en curso nunca cambia de
    /// contenido; solo una rechazada (o aún no enviada) se reemplaza al replanificar con los datos corregidos.
    /// </summary>
    public string DocumentJson { get; set; } = "";

    public string Status { get; set; } = TransportGuideStatuses.Planned;
    public long? Folio { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? IssuedAt { get; set; }

    public List<TransportGuideDocument> Documents { get; set; } = new();
}

/// <summary>Documento del manifiesto (factura) que ampara una guía; va como referencia en el DTE.</summary>
public class TransportGuideDocument
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid TransportGuideId { get; set; }
    public Guid ManifestDocumentId { get; set; }
}
