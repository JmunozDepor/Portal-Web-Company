using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// CRUD de la configuración de Modulo.ImportacionGenerica de la organización actual --
/// ver GenericImportConfigDto para el criterio de estándar (BusinessPartnerCardCode
/// null) vs excepción por cliente/proveedor. Portado de
/// IConfiguracionImportacionGenericaService.
/// </summary>
public interface IGenericImportConfigService
{
    Task<IReadOnlyList<GenericImportConfigDto>> ListAsync(GenericImportModule? module = null, CancellationToken ct = default);

    Task<GenericImportConfigDto?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Resuelve la configuración vigente para el socio de negocio indicado: primero
    /// busca su excepción puntual (BusinessPartnerCardCode = businessPartnerCardCode),
    /// y si no existe (o no está IsActive), cae al estándar de la organización
    /// (BusinessPartnerCardCode IS NULL). Null si ninguna de las dos existe.
    /// </summary>
    Task<GenericImportConfigDto?> ResolveAsync(GenericImportModule module, string documentType,
        GenericImportLineType lineType, string? businessPartnerCardCode, CancellationToken ct = default);

    /// <summary>Reemplaza por completo el detalle de campos (borra y vuelve a crear) -- evita diffing, el volumen por configuración es chico.</summary>
    Task<int> CreateAsync(GenericImportModule module, string documentType, GenericImportLineType lineType,
        string? businessPartnerCardCode, string? groupingColumn, bool skuIsCustomerOwn, string alias,
        IReadOnlyList<GenericImportConfigFieldDto> fields,
        GenericImportPriceSource priceSource = GenericImportPriceSource.BusinessPartner, int? systemPriceListCode = null,
        CancellationToken ct = default);

    Task UpdateAsync(int id, string? groupingColumn, bool skuIsCustomerOwn, string alias, bool isActive,
        IReadOnlyList<GenericImportConfigFieldDto> fields,
        GenericImportPriceSource priceSource = GenericImportPriceSource.BusinessPartner, int? systemPriceListCode = null,
        CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}
