namespace Modulo.Wms.Models;

/// <summary>
/// Qué campos, al cambiar de VALOR (no de fecha), disparan Status=Pendiente en el
/// staging de una entidad -- ver spec 2026-08-21-ingesta-sql-directa-staging-items-design.md.
/// Mismo patrón que WmsFieldMapping (Fase Subida) pero para el lado Bajada.
/// </summary>
public sealed class WmsValidationField
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TipoEntidad { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
