namespace Modulo.Wms.Models;

public enum WmsSvshStatus { Pendiente, ProcesadoSap, ErrorSap }

/// <summary>
/// Confirmación WMS -> SAP de recepción de un ASN/traslado/devolución --
/// aplanado del XML de Oracle WMS Cloud (Header -> ib_shipment ->
/// ib_shipment_hdr + ib_shipment_dtl[], ver SVSH_Processor.cs en WMS_Suite),
/// una fila por línea de detalle. Espejo del patrón WmsOracleStageSlsh.
/// </summary>
public class WmsOracleStageSvsh
{
    public long LineId { get; set; }
    public long ParentId { get; set; }
    public WmsSvshStatus Status { get; set; } = WmsSvshStatus.Pendiente;
    public string? ErrorMsg { get; set; }
    public int RetryCount { get; set; }
    public int? SapDocEntry { get; set; }
    public int? SapObject { get; set; }

    // Header global (Header) y header del shipment (ib_shipment_hdr).
    public string? DocumentVersion { get; set; }
    public string? OriginSystem { get; set; }
    public string? ClientEnvCode { get; set; }
    public string? ParentCompanyCode { get; set; }
    public string? Entity { get; set; }
    public string? TimeStamp { get; set; }
    public string? MessageId { get; set; }
    public string? shipment_nbr { get; set; }
    public string? manifest_nbr { get; set; }
    public string? load_nbr { get; set; }
    public string? facility_code { get; set; }
    public string? company_code { get; set; }
    public string? asn_nbr { get; set; }
    public string? carrier_code { get; set; }
    public string? trailer_nbr { get; set; }
    public string? seal_nbr { get; set; }
    public string? rcvd_date { get; set; }
    public string? rcvd_date_time { get; set; }
    public string? cust_nbr { get; set; }
    public string? vendor_nbr { get; set; }
    public string? order_nbr { get; set; }
    public string? customer_po_nbr { get; set; }

    // Detalle (ib_shipment_dtl) -- usados por el reader para decidir endpoint SAP y línea.
    public string? shipment_dtl_cust_field_1 { get; set; } // BaseType
    public string? shipment_dtl_cust_field_2 { get; set; } // BaseEntry
    public string? shipment_dtl_cust_field_3 { get; set; } // LineNum
    public string? item_part_a { get; set; }
    public string? item_part_b { get; set; }
    public string? item_alternate_code { get; set; }
    public string? received_qty { get; set; }
    public string? shipped_qty { get; set; }
    public string? shipped_uom { get; set; }
    public string? ib_lpn_nbr { get; set; }
    public string? batch_nbr { get; set; }
    public string? expiry_date { get; set; }
    public string? serial_nbr { get; set; }
    public string? line_nbr { get; set; }
    public string? seq_nbr { get; set; }
}
