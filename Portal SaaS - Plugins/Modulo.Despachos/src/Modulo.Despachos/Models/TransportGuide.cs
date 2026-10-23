namespace Modulo.Despachos.Models;

/// <summary>
/// Guía de despacho electrónica (DTE 52, Res. Ex. SII N°154) planificada para un manifiesto: una por destino
/// (RUT receptor + dirección + comuna), dividida si excede los límites del formato. Se planifica una sola vez,
/// en el primer intento de despacho, y los reintentos reusan la misma <see cref="EmissionKey"/> (idempotencia en
/// la pasarela DTE de la plataforma, que guarda folio, XML y estado con el proveedor en dte_emissions).
/// </summary>
public class TransportGuide
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ManifestId { get; set; }

    /// <summary>Orden dentro del manifiesto (1..N). Índice único (manifest_id, sequence).</summary>
    public int Sequence { get; set; }

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
