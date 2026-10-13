using System.Text.Json;
using System.Text.Json.Serialization;
using B1SLayer;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Sap;

/// <summary>
/// Adaptador de B1SLayer.SLConnection a la interfaz agnóstica ISapSession, para que
/// Abstractions no dependa de B1SLayer. Portado de PortalSAP_v2 (SapSession) tal cual.
/// </summary>
public sealed class SapSession : ISapSession
{
    private readonly SLConnection _connection;

    public SapSession(SLConnection connection)
    {
        _connection = connection;
    }

    public async Task<T?> GetAsync<T>(string recurso, string? filtroOData = null, string? expandOData = null, CancellationToken ct = default)
    {
        var request = _connection.Request(recurso);
        if (!string.IsNullOrWhiteSpace(filtroOData))
        {
            request = request.Filter(filtroOData);
        }
        if (!string.IsNullOrWhiteSpace(expandOData))
        {
            request = request.Expand(expandOData);
        }

        return await request.GetAsync<T>();
    }

    public async Task<IReadOnlyList<T>> GetAllAsync<T>(string recurso, string? filtroOData = null, string? expandOData = null, string? selectOData = null, int? topPorPagina = null, CancellationToken ct = default)
    {
        var request = _connection.Request(recurso);
        if (!string.IsNullOrWhiteSpace(filtroOData))
        {
            request = request.Filter(filtroOData);
        }
        if (!string.IsNullOrWhiteSpace(expandOData))
        {
            request = request.Expand(expandOData);
        }
        if (!string.IsNullOrWhiteSpace(selectOData))
        {
            request = request.Select(selectOData);
        }
        if (topPorPagina is > 0)
        {
            request = request.Top(topPorPagina.Value);
        }

        // SLRequest.GetAllAsync<T>() (B1SLayer) sigue "odata.nextLink" de Service Layer
        // internamente hasta agotar la paginación -- evita reimplementar un loop manual
        // de $skip acá (más simple y menos propenso a error que reinventarlo). Top() acá fija
        // el tamaño de CADA página, no un límite total.
        var todas = await request.GetAllAsync<T>();
        return todas as IReadOnlyList<T> ?? todas.ToList();
    }

    public async Task<T?> PostAsync<T>(string recurso, object cuerpo, CancellationToken ct = default)
        => await _connection.Request(recurso).PostAsync<T>(cuerpo);

    public async Task PostAsync(string recurso, object cuerpo, CancellationToken ct = default)
        => await _connection.Request(recurso).PostAsync(cuerpo);

    public async Task PatchAsync(string recurso, object clave, object cuerpo, CancellationToken ct = default)
        => await _connection.Request(recurso, clave).PatchAsync(cuerpo);

    public async Task DeleteAsync(string recurso, CancellationToken ct = default)
        => await _connection.Request(recurso).DeleteAsync();

    // Mismas opciones que B1SLayer usa por defecto en SLBatchRequest (nulls omitidos) --
    // el JSON se serializa UNA vez acá y se envía como string, así lo que se muestra en
    // pantalla es exactamente lo que recibió Service Layer.
    private static readonly JsonSerializerOptions SimulationJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public async Task<SapSimulationResult> SimulatePostAsync(string recurso, object cuerpo, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(cuerpo, cuerpo.GetType(), SimulationJsonOptions);

        // Centinela: PATCH a un artículo cuyo código supera el largo máximo de OITM.ItemCode
        // (50) -- no puede existir, así que SIEMPRE falla y Service Layer revierte el
        // changeset completo, documento incluido. Ver SapSimulationResponseInterpreter.
        var sentinelKey = "PORTAL-SIMULACION-ROLLBACK-" + Guid.NewGuid().ToString("N");
        var requests = new[]
        {
            new SLBatchRequest(HttpMethod.Post, recurso, json, 1).WithReturnNoContent(),
            new SLBatchRequest(HttpMethod.Patch, $"Items('{sentinelKey}')", new { ItemName = "x" }, 2),
        };

        try
        {
            var responses = await _connection.PostBatchAsync(requests, singleChangeSet: true);
            var parts = new List<SapSimulationResponseInterpreter.BatchPart>();
            foreach (var response in responses)
            {
                var body = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(ct);
                var contentId = response.Headers.TryGetValues("Content-ID", out var ids) ? ids.FirstOrDefault()
                    : response.Content?.Headers.TryGetValues("Content-ID", out var contentIds) == true ? contentIds.FirstOrDefault()
                    : null;
                parts.Add(new SapSimulationResponseInterpreter.BatchPart((int)response.StatusCode, contentId?.Trim(), body));
            }

            var (outcome, message) = SapSimulationResponseInterpreter.Interpret(parts);
            return new SapSimulationResult(outcome, message, json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SapSimulationResult(SapSimulationOutcome.Inconclusive, $"No se pudo completar el envío simulado: {ex.Message}", json);
        }
    }
}
