using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// CRUD del catálogo maestro de campos de usuario de la COMPAÑÍA activa (ver
/// ICurrentCompanyAccessor.CompanyId -- regla dura de que todo plugin que dependa de SAP
/// se personaliza por Company, no por Organization: un UDF es propio de la base física
/// de cada Company). Portado de ICampoUsuarioImportacionGenericaService.
/// </summary>
public interface IGenericImportUserFieldService
{
    Task<IReadOnlyList<GenericImportUserFieldDto>> ListAsync(GenericImportModule? module = null, CancellationToken ct = default);

    Task<GenericImportUserFieldDto?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Rechaza con InvalidOperationException si sapFieldName coincide con un campo
    /// núcleo reservado (ver SapAdditionalFieldsHelper.IsReservedName) -- un campo de
    /// usuario nunca puede pisar la lógica ya resuelta por el motor.
    /// </summary>
    Task<int> CreateAsync(GenericImportModule module, GenericImportFieldLevel level, string label,
        string sapFieldName, GenericImportFieldDataType dataType, CancellationToken ct = default);

    Task UpdateAsync(int id, string label, string sapFieldName, GenericImportFieldDataType dataType,
        bool isActive, CancellationToken ct = default);

    Task DeleteAsync(int id, CancellationToken ct = default);
}
