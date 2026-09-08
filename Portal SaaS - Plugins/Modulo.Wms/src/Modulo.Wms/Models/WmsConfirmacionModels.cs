namespace Modulo.Wms.Models;

public class WmsConfirmacionRow
{
    public string Documento { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int LineasCount { get; set; }
    public string? ErrorMsg { get; set; }
    public List<long> LineIds { get; set; } = new();
}

public class WmsConfirmacionFiltro
{
    public WmsTipoTransaccion Tipo { get; set; }
    public string? Estado { get; set; }
    public string? Documento { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}
