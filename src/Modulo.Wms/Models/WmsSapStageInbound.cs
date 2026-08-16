namespace Modulo.Wms.Models;

public class WmsSapStageInboundHdr
{
    public long LineId { get; set; }
    public Guid CompanyId { get; set; }
    public int SapDocEntry { get; set; }
    public string ShipmentType { get; set; } = string.Empty;
    public DateTime SourceUpdateDate { get; set; }
    public WmsSapStageStatus Status { get; set; } = WmsSapStageStatus.Pendiente;
    public int RetryCount { get; set; }
    public string? ErrorMsg { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; set; }
}

public class WmsSapStageInboundDtl
{
    public long LineId { get; set; }
    public long ParentId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string WhsCode { get; set; } = string.Empty;
    public int LineNum { get; set; }
}
