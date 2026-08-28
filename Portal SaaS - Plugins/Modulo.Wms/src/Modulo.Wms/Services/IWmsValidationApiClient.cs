namespace Modulo.Wms.Services;

public interface IWmsValidationApiClient
{
    Task<WmsStageCheckResult> CheckStageRecordAsync(
        string lgfApiBaseUrl, string usuario, string clave,
        string entity, string keyField, string keyValue, string? companyCode,
        bool filtrarPorUrl, CancellationToken ct = default);
}

public sealed record WmsStageCheckResult(bool Found, int? StatusId, string? ErrorMessage);
