using B1SLayer;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Sap.Data.Hana;

namespace PortalSaas.Core.Sap;

/// <summary>
/// Implementación real de ISapConnectionTestService -- ver su doc-comment. Corre las dos
/// pruebas en paralelo (son independientes) y nunca deja escapar la excepción cruda al
/// caller: el mensaje de error real (incluida la del driver nativo de HANA) es
/// justamente lo que un administrador necesita ver para diagnosticar una compañía mal
/// configurada, así que se captura y se devuelve como texto en el resultado.
/// </summary>
public sealed class SapConnectionTestService : ISapConnectionTestService
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secrets;

    public SapConnectionTestService(PortalSaasDbContext db, ISecretoCifradoService secrets)
    {
        _db = db;
        _secrets = secrets;
    }

    public async Task<SapConnectionTestResult> TestAsync(Guid companyId, CancellationToken ct = default)
    {
        var company = await _db.Companies
            .Include(c => c.Instance)
            .FirstOrDefaultAsync(c => c.Id == companyId, ct)
            ?? throw new InvalidOperationException($"Compañía '{companyId}' no encontrada.");

        var databaseTask = TestDatabaseAsync(company, ct);
        var serviceLayerTask = TestServiceLayerAsync(company, ct);
        await Task.WhenAll(databaseTask, serviceLayerTask);

        var (databaseSuccess, databaseError) = databaseTask.Result;
        var (serviceLayerSuccess, serviceLayerError) = serviceLayerTask.Result;

        return new SapConnectionTestResult(databaseSuccess, databaseError, serviceLayerSuccess, serviceLayerError);
    }

    private async Task<(bool Success, string? Error)> TestDatabaseAsync(Company company, CancellationToken ct)
    {
        try
        {
            var (engineType, connectionString) = SapConnectionStringFactory.Build(company, _secrets);

            if (engineType == SapEngineType.SqlServer)
            {
                using var conn = new SqlConnection(connectionString);
                using var cmd = new SqlCommand("SELECT 1", conn);
                await conn.OpenAsync(ct);
                await cmd.ExecuteScalarAsync(ct);
            }
            else
            {
                using var conn = new HanaConnection(connectionString);
                using var cmd = new HanaCommand("SELECT 1 FROM DUMMY", conn);
                await conn.OpenAsync(ct);
                await cmd.ExecuteScalarAsync(ct);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<(bool Success, string? Error)> TestServiceLayerAsync(Company company, CancellationToken ct)
    {
        try
        {
            var password = _secrets.Decrypt(company.IntegrationSecretKey);
            var connection = new SLConnection(company.ServiceLayerUrl, company.DatabaseName, company.IntegrationUsername, password);
            await connection.LoginAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
