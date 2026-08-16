namespace Modulo.Wms.Models;

public class WmsSapStageOrderHdr
{
    public long LineId { get; set; }
    public Guid CompanyId { get; set; }
    public string OrderNbr { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public int PickListAbsEntry { get; set; }
    public int BaseObjectType { get; set; }
    public int BaseEntry { get; set; }
    public string CardCode { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string? CustomerPoNbr { get; set; }
    public DateTime? OrdDate { get; set; }
    public DateTime? ExpDate { get; set; }
    public DateTime? ReqShipDate { get; set; }
    public string? ShipToCode { get; set; }
    public DateTime SourceUpdateDate { get; set; }
    public WmsSapStageStatus Status { get; set; } = WmsSapStageStatus.Pendiente;
    public int RetryCount { get; set; }
    public string? ErrorMsg { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; set; }
}

public class WmsSapStageOrderDtl
{
    public long LineId { get; set; }
    public long ParentId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? WhsCode { get; set; }
    public int LineNum { get; set; }
    public int SeqNbr { get; set; }
}
