using System.Text.Json;
using System.Text.Json.Serialization;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Compras;
using PortalSaas.Core.Sap;

namespace PortalSaas.Core.Tests.Compras;

/// <summary>Guarda el último SQL y sus parámetros; QueryAsync&lt;T&gt; devuelve las filas precargadas para T.</summary>
internal sealed class FakeHana : IHanaService
{
    private readonly Dictionary<Type, object> _rows = new();

    public string? LastSql { get; private set; }
    public object? LastParameters { get; private set; }

    public FakeHana With<T>(params T[] rows)
    {
        _rows[typeof(T)] = rows.ToList();
        return this;
    }

    /// <summary>Valor de un parámetro, venga como diccionario o como objeto anónimo.</summary>
    public object? Param(string name) => LastParameters switch
    {
        IReadOnlyDictionary<string, object?> dict => dict.TryGetValue(name, out var value) ? value : null,
        null => null,
        var obj => obj.GetType().GetProperty(name)?.GetValue(obj),
    };

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sqlParametrizado, object? parametros = null, CancellationToken ct = default)
    {
        LastSql = sqlParametrizado;
        LastParameters = parametros;
        if (_rows.TryGetValue(typeof(T), out var rows))
        {
            return Task.FromResult((IReadOnlyList<T>)rows);
        }

        IReadOnlyList<T> empty = typeof(T) == typeof(int) ? [(T)(object)0] : [];
        return Task.FromResult(empty);
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> ExecuteAsync(string sqlParametrizado, object? parametros = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string> GetPlatformEngineTypeAsync(CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>Guarda el POST de creación y devuelve el documento precargado en GetAsync.</summary>
internal sealed class FakeSession : ISapSession
{
    public string? PostedResource { get; private set; }
    public object? PostedBody { get; private set; }
    public object? Document { get; set; }

    public Task<T?> GetAsync<T>(string recurso, string? filtroOData = null, string? expandOData = null, CancellationToken ct = default) =>
        Task.FromResult((T?)Document);

    public Task<IReadOnlyList<T>> GetAllAsync<T>(string recurso, string? filtroOData = null, string? expandOData = null, string? selectOData = null, int? topPorPagina = null, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<T?> PostAsync<T>(string recurso, object cuerpo, CancellationToken ct = default)
    {
        PostedResource = recurso;
        PostedBody = cuerpo;
        return Task.FromResult((T?)(object)new SapPurchaseDocumentHeader { DocEntry = 99 });
    }

    public Task PostAsync(string recurso, object cuerpo, CancellationToken ct = default) => throw new NotSupportedException();
    public Task PatchAsync(string recurso, object clave, object cuerpo, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAsync(string recurso, CancellationToken ct = default) => throw new NotSupportedException();

    /// <summary>El cuerpo como lo vería Service Layer: sin las propiedades null.</summary>
    public JsonElement Body() =>
        JsonSerializer.SerializeToElement(PostedBody, PostedBody!.GetType(),
            new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
}

internal sealed class FakeProvider(ISapSession session) : ISapConnectionProvider
{
    public Task<ISapSession> GetConnectionAsync(CancellationToken ct = default) => Task.FromResult(session);
}

internal sealed class AllowDefault : IOrganizationDocumentPermissionService
{
    public Task<bool> IsCreateAllowedAsync(string engine, string documentType, bool defaultValue, CancellationToken ct = default) =>
        Task.FromResult(defaultValue);
}

internal sealed class DefaultUdf : ISapTraceabilityFieldResolver
{
    public Task<string> ResolveUserFieldNameAsync(CancellationToken ct = default) =>
        Task.FromResult(SapTraceabilityFieldResolver.DefaultUserFieldName);
}
