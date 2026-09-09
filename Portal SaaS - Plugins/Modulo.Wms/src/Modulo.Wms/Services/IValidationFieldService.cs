using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

/// <summary>
/// CRUD de <c>wms_validation_fields</c>: qué campos, al cambiar de VALOR entre
/// corridas de Bajada, re-marcan una fila de staging como Pendiente (lado Bajada,
/// espejo de IFieldMappingService que es lado Subida). Lo consumen
/// WmsSapStageItemWriter (TipoEntidad="Item") y WmsSapStageStoreWriter ("Store").
/// </summary>
public interface IValidationFieldService
{
    /// <summary>Tipos de entidad soportados hoy por un Writer -- la página solo deja elegir estos.</summary>
    static readonly IReadOnlyList<string> TiposEntidad = new[] { "Item", "Store" };

    Task<IReadOnlyList<WmsValidationField>> ListAllAsync(Guid companyId, CancellationToken ct = default);

    Task<long> CreateAsync(Guid companyId, string tipoEntidad, string fieldName, bool isActive, CancellationToken ct = default);

    Task SetActiveAsync(long id, Guid companyId, bool isActive, CancellationToken ct = default);

    Task DeleteAsync(long id, Guid companyId, CancellationToken ct = default);
}
