using B1SLayer;
using PortalSaas.Abstractions.Contratos;

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

    public async Task<IReadOnlyList<T>> GetAllAsync<T>(string recurso, string? filtroOData = null, string? expandOData = null, CancellationToken ct = default)
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

        // SLRequest.GetAllAsync<T>() (B1SLayer) sigue "odata.nextLink" de Service Layer
        // internamente hasta agotar la paginación -- evita reimplementar un loop manual
        // de $skip acá (más simple y menos propenso a error que reinventarlo).
        var todas = await request.GetAllAsync<T>();
        return todas as IReadOnlyList<T> ?? todas.ToList();
    }

    public async Task<T?> PostAsync<T>(string recurso, object cuerpo, CancellationToken ct = default)
        => await _connection.Request(recurso).PostAsync<T>(cuerpo);

    public async Task PatchAsync(string recurso, object clave, object cuerpo, CancellationToken ct = default)
        => await _connection.Request(recurso, clave).PatchAsync(cuerpo);

    public async Task DeleteAsync(string recurso, CancellationToken ct = default)
        => await _connection.Request(recurso).DeleteAsync();
}
