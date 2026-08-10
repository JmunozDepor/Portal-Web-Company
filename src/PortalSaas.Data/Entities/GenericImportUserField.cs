namespace PortalSaas.Data.Entities;

/// <summary>
/// Catálogo maestro de campos de usuario (UDF dinámicos) de Modulo.ImportacionGenerica,
/// por COMPAÑÍA -- equivalente a CAMPO_USUARIO_IMPORTACION_GENERICA en
/// referencia-original/PortalSAP_v2, pero vive en la base propia de la plataforma
/// (company_id), no en HANA -- mismo criterio que OrganizationDocumentPermission.
///
/// CompanyId, no OrganizationId (regla dura, ver GenericImportConfig y CLAUDE.md) -- un
/// UDF es una particularidad física de la base SAP de ESA Company (confirmado en la
/// referencia-original, sección "UDF son por compañía SAP, no por el catálogo del
/// portal": un campo de usuario dado de alta en una Company puede no existir todavía en
/// otra Company de la misma Organization). El catálogo organization-wide del original
/// era justamente la causa de ese problema operativo -- acá se corrige de raíz en vez de
/// heredarlo.
///
/// Module/Level/DataType son string libre, no un enum -- PortalSaas.Data nunca
/// referencia PortalSaas.Abstractions. Ver
/// PortalSaas.Core.ImportacionGenerica.GenericImportEnumNames para los valores reales
/// (espejo de GenericImportModule/GenericImportFieldLevel/GenericImportFieldDataType.ToString()).
/// </summary>
public sealed class GenericImportUserField
{
    public int Id { get; set; }

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

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
