namespace Modulo.Despachos.Models;

/// <summary>Documento SAP (factura / guía) dentro de un manifiesto, con los datos copiados al agregarlo.</summary>
public class ManifestDocument
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ManifestId { get; set; }

    public string DocumentKind { get; set; } = DocumentKinds.Invoice;
    public int SiiDocumentType { get; set; }
    public long Folio { get; set; }
    public int SapDocEntry { get; set; }
    public int SapDocNum { get; set; }
    public DateOnly DocumentDate { get; set; }
    public decimal TotalAmount { get; set; }

    public string CustomerCode { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string? CustomerTaxId { get; set; }

    public string? DestinationAddress { get; set; }
    /// <summary>Texto de comuna tal como vino de SAP (se conserva aunque no se resuelva contra el catálogo).</summary>
    public string? DestinationCommuneName { get; set; }
    /// <summary>Null = comuna no resuelta (la Etapa 2 exigirá corregirla).</summary>
    public long? DestinationCommuneId { get; set; }

    /// <summary>LPN de la transferencia (OWTR.U_NX_LPN); solo DocumentKind = transfer.</summary>
    public string? Lpn { get; set; }

    /// <summary>Escritura del folio en la transferencia SAP (SapWritebackStatuses) y su último error.</summary>
    public string? SapWritebackStatus { get; set; }
    public string? SapWritebackError { get; set; }

    /// <summary>Pre-guía de la que entró la transferencia (escaneando su hoja); null si entró suelta.</summary>
    public long? DispatchGroupId { get; set; }

    public int? SapBaseOrderEntry { get; set; }
    public string? CustomerPo { get; set; }
    public decimal Units { get; set; }
    public int Packages { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }

    public string DeliveryStatus { get; set; } = DeliveryStatuses.Pending;
    public long? RejectionReasonId { get; set; }
    public string? Comments { get; set; }
    public Guid? ReturnedBy { get; set; }
    public DateTime? ReturnedAt { get; set; }

    public string CaptureMethod { get; set; } = CaptureMethods.Manual;
    public DateTime AddedAt { get; set; }
    public Guid? AddedBy { get; set; }
}
