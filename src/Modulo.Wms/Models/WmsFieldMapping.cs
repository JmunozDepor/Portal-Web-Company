namespace Modulo.Wms.Models;

/// <summary>
/// Mapeo de campos por documento, Fase 1 -- portado de INT_SAP_FIELD_MAPPING
/// (WMS_Suite, WmsPortal.Web > Admin > Mapeo de Campos SAP). Mismo lenguaje de
/// templates que la tabla real de producción ({lpn}, {header.MessageId},
/// concatenación con " ; "), tal cual, no reescrito -- ver ARQUITECTURA.md.
///
/// MapperKey hoy cubre solo la dirección WMS -> SAP (confirmación de órdenes/
/// ingresos, un MapperKey fijo por método hardcodeado en el processor -- ver
/// ARQUITECTURA.md, corrección sobre el alcance real de esta tabla). La
/// generalización a mapeo de campos estándar SAP -> WMS (Fase 2) reutiliza esta
/// misma tabla, no una paralela.
/// </summary>
public sealed class WmsFieldMapping
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public string MapperKey { get; set; } = null!;
    public string FieldName { get; set; } = null!;
    public string ValueTemplate { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? UpdatedBy { get; set; }
}
