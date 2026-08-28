using B1SLayer;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Data;

namespace PortalSaas.Core.Sap;

/// <summary>
/// Resuelve la sesión de Service Layer usando el usuario de INTEGRACIÓN de la compañía
/// activa (patrón híbrido -- nunca la contraseña del usuario del portal). La sesión se
/// cachea por Company.Id (ver ISapSessionCache) -- el usuario de integración es el mismo
/// para todos los usuarios web de esa compañía, así que no hace falta cachear por sesión
/// HTTP. Portado de PortalSAP_v2 (SapConnectionProvider); resuelve Company vía EF Core
/// directo en vez de EmpresaRepositorio (ver el doc-comment de HanaService).
/// </summary>
public sealed class SapConnectionProvider : ISapConnectionProvider
{
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ISecretoCifradoService _secrets;
    private readonly PortalSaasDbContext _db;
    private readonly ISapSessionCache _cache;

    public SapConnectionProvider(ICurrentCompanyAccessor currentCompany, ISecretoCifradoService secrets,
        PortalSaasDbContext db, ISapSessionCache cache)
    {
        _currentCompany = currentCompany;
        _secrets = secrets;
        _db = db;
        _cache = cache;
    }

    /// <summary>SLConnection.LoginAsync no acepta CancellationToken y puede colgarse
    /// indefinidamente (ej. handshake TLS lento, SAP no responde) -- IntegrationSyncHostedService
    /// procesa las integraciones en un único loop secuencial, así que sin este límite una
    /// conexión colgada congela TODAS las integraciones de la compañía para siempre (encontrado
    /// 21 ago 2026: el ciclo hizo una sola pasada al arrancar y nunca más volvió a hacer
    /// polling). Task.WaitAsync no cancela la llamada colgada en sí (LoginAsync sigue corriendo
    /// en segundo plano), pero libera el loop para que siga con el resto y marque esta
    /// integración como Error en vez de trabarse para siempre.</summary>
    private static readonly TimeSpan TimeoutLogin = TimeSpan.FromSeconds(30);

    public async Task<ISapSession> GetConnectionAsync(CancellationToken ct = default)
    {
        var companyId = _currentCompany.CompanyId;

        var connection = await _cache.GetOrCreateAsync(companyId, async () =>
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId && c.IsActive, ct)
                ?? throw new InvalidOperationException($"Compañía '{companyId}' no encontrada/activa.");

            var password = _secrets.Decrypt(company.IntegrationSecretKey);

            var slConnection = new SLConnection(company.ServiceLayerUrl, company.DatabaseName, company.IntegrationUsername, password);
            try
            {
                await slConnection.LoginAsync().WaitAsync(TimeoutLogin, ct);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(
                    $"SAP Service Layer no respondió el login en {TimeoutLogin.TotalSeconds}s (compañía '{companyId}', URL '{company.ServiceLayerUrl}').");
            }
            return slConnection;
        });

        return new SapSession(connection);
    }
}
