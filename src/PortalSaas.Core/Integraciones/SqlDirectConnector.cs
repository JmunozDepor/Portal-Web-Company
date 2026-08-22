using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace PortalSaas.Core.Integraciones;

/// <summary>
/// Conector de ingesta por SQL directo (HANA/SQL Server, vía IHanaService) -- reemplaza
/// Service Layer para entidades cuyo mapeo real requiere joins que OData no puede
/// expresar (ver spec 2026-08-21-ingesta-sql-directa-staging-items-design.md). La query
/// la escribe un admin en /Admin/Integraciones/Nuevo; el conector solo reemplaza el
/// placeholder ":cursor" por el valor real y ejecuta.
/// </summary>
public class SqlDirectConnector : IIntegrationConnector
{
    private readonly IHanaService _hanaService;

    public SqlDirectConnector(IHanaService hanaService)
    {
        _hanaService = hanaService;
    }

    public string Tipo => "Sql";

    private sealed record SqlDirectConfig(string Query);

    public async Task<IReadOnlyList<IntegrationRecord>> PullAsync(
        string conectorConfigJson,
        DateTimeOffset? cursorIncremental,
        CancellationToken cancellationToken)
    {
        SqlDirectConfig? config;
        try
        {
            config = System.Text.Json.JsonSerializer.Deserialize<SqlDirectConfig>(conectorConfigJson);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Config de conector Sql inválida: {ex.Message}", ex);
        }

        if (config is null)
        {
            throw new InvalidOperationException("Config de conector Sql inválida o vacía.");
        }

        var filas = await _hanaService.QueryDynamicAsync(
            config.Query,
            new Dictionary<string, object?> { ["cursor"] = cursorIncremental?.UtcDateTime },
            cancellationToken);

        return filas
            .Select(fila => new IntegrationRecord(new Dictionary<string, object?>(fila, StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    public string DescribirConsulta(string conectorConfigJson, DateTimeOffset? cursorIncremental = null)
    {
        SqlDirectConfig? config;
        try
        {
            config = System.Text.Json.JsonSerializer.Deserialize<SqlDirectConfig>(conectorConfigJson);
        }
        catch (Exception ex)
        {
            return $"Config de conector Sql inválida: {ex.Message}";
        }

        if (config is null)
        {
            return "Config de conector Sql vacía.";
        }

        var cursorTexto = cursorIncremental?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss") ?? "(sin cursor, primera corrida)";
        return $"SQL directo (:cursor={cursorTexto}): {config.Query}";
    }

    public Task<IReadOnlyList<IntegrationPushResult>> PushAsync(
        string conectorConfigJson,
        IReadOnlyList<IntegrationRecord> registros,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("SqlDirectConnector.PushAsync no está implementado -- este conector solo lee (Bajada), nunca escribe hacia SAP.");
}
