namespace PortalSaas.Data.Entities;

/// <summary>
/// Catálogo maestro de campos de usuario (UDF dinámicos) de Modulo.ImportacionGenerica,
/// por organización -- solo administrador de organización. Equivalente a
/// CAMPO_USUARIO_IMPORTACION_GENERICA en referencia-original/PortalSAP_v2, pero vive en
/// la base propia de la plataforma (organization_id), no en HANA -- mismo criterio que
/// OrganizationDocumentPermission.
///
/// Module/Level/DataType son string libre, no un enum -- PortalSaas.Data nunca
/// referencia PortalSaas.Abstractions. Ver
/// PortalSaas.Core.ImportacionGenerica.GenericImportEnumNames para los valores reales
/// (espejo de GenericImportModule/GenericImportFieldLevel/GenericImportFieldDataType.ToString()).
/// </summary>
public sealed class GenericImportUserField
{
    public int Id { get; set; }

    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    /// <summary>"Sales" | "Purchase" | "Inventory".</summary>
    public string Module { get; set; } = null!;

    /// <summary>"Header" | "Line".</summary>
    public string Level { get; set; } = null!;

    public string Label { get; set; } = null!;

    /// <summary>Nombre real de la propiedad de Service Layer -- UDF propio o cualquier propiedad estándar que todavía no sea un campo núcleo.</summary>
    public string SapFieldName { get; set; } = null!;

    /// <summary>"Text" | "Number" | "Date".</summary>
    public string DataType { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}
