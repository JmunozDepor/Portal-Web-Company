namespace Servicios.Common.Contratos;

/// <summary>
/// Único punto de entrada para saber contra qué compañías SAP debe correr un servicio de
/// esta familia. Hoy la única implementación es <c>AppSettingsCompanyProvider</c> (lista
/// fija en appsettings.json, equivalente al SociedadSetting del servicio legado). El día
/// que este proyecto se integre a Proyecto Saas Portal, una implementación nueva
/// (PlatformOrganizationCompanyProvider, todavía no escrita) leerá organizations/
/// companies/instances de esa base -- ningún consumidor de este contrato debería
/// necesitar cambios ese día.
/// </summary>
public interface ICompanyProvider
{
    Task<IReadOnlyList<CompanyConnectionConfig>> GetActiveCompaniesAsync(CancellationToken ct);
}
