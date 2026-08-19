using Microsoft.Extensions.Options;
using Servicios.Common.Contratos;

namespace Servicios.Common.Configuracion;

/// <summary>
/// Implementación de ICompanyProvider de hoy: lee la lista de compañías directo de
/// appsettings.json (sección "Sociedades"). Sirve igual para 1 compañía (modo standalone)
/// que para N (modo multi-empresa) -- no hay bifurcación de código entre los dos casos,
/// ver CLAUDE.md. El día que este proyecto se integre a Proyecto Saas Portal, esta clase
/// se reemplaza (o convive detrás del mismo ICompanyProvider) por una que lea
/// organizations/companies/instances de esa base.
/// </summary>
public sealed class AppSettingsCompanyProvider : ICompanyProvider
{
    private readonly IOptions<List<SociedadSetting>> _sociedades;

    public AppSettingsCompanyProvider(IOptions<List<SociedadSetting>> sociedades)
    {
        _sociedades = sociedades;
    }

    public Task<IReadOnlyList<CompanyConnectionConfig>> GetActiveCompaniesAsync(CancellationToken ct)
    {
        var resultado = _sociedades.Value
            .Where(s => s.IsActive)
            .Select(s => new CompanyConnectionConfig
            {
                CompanyCode = s.CompanyCode,
                EngineType = s.ResolverEngineType(),
                Host = s.Host,
                Port = s.Port,
                DatabaseEncryptada = s.DatabaseEncryptada,
                Schema = s.Schema,
                DbUserId = s.DbUserId,
                DbSecretCifrado = s.DbSecreto,
                ServiceLayerUrl = s.ServiceLayerUrl,
                ServiceLayerUsername = s.ServiceLayerUsername,
                ServiceLayerSecretCifrado = s.ServiceLayerSecreto,
                ToleraNombreCertificadoServiceLayer = s.ToleraNombreCertificadoServiceLayer,
                ConfiaCertificadoServiceLayer = s.ConfiaCertificadoServiceLayer,
                WarehousePriorityCount = s.WarehousePriorityCount,
                HeaderQuerySource = s.HeaderQuerySource,
                WarehouseAssignmentProcedure = s.WarehouseAssignmentProcedure,
                CompletionUdfFieldName = s.CompletionUdfFieldName,
                WarehousePriorityTable = s.WarehousePriorityTable,
                PickingPendingQuery = s.PickingPendingQuery,
                ConfiaCertificadoBaseDatos = s.ConfiaCertificadoBaseDatos
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<CompanyConnectionConfig>>(resultado);
    }
}
