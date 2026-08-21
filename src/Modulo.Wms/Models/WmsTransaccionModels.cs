namespace Modulo.Wms.Models;

public class WmsTransaccionFiltro
{
    public WmsTipoTransaccion Tipo { get; set; }
    public string? Estado { get; set; }
    public string? Documento { get; set; }
    public DateTime? Desde { get; set; }
    public DateTime? Hasta { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public class WmsTransaccionRow
{
    public long LineId { get; set; }
    public string Documento { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public string? ErrorMsg { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
}

public class WmsPagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}
