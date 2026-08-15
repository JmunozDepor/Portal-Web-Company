namespace Modulo.Wms.Models;

public enum WmsInboundFormato { Xml, Json, Txt }
public enum WmsInboundEstado { Pendiente, Aplanado, ErrorEstructura, ErrorStaging }

public class WmsOracleInboundStage
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TipoDoc { get; set; } = string.Empty;
    public WmsInboundFormato Formato { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string HashArchivo { get; set; } = string.Empty;
    public string Contenido { get; set; } = string.Empty;
    public WmsInboundEstado Estado { get; set; } = WmsInboundEstado.Pendiente;
    public int Intentos { get; set; }
    public string? MensajeError { get; set; }
    public string? SapDocEntry { get; set; }
    public DateTimeOffset InsertedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
}
