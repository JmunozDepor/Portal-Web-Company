using System.Globalization;
using System.Text.Json;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Servicios;

/// <summary>
/// <see cref="ISapTransferWriter"/> (transferencias y Entregas) sobre la sesión de Service Layer de la compañía activa
/// (<see cref="ISapConnectionProvider"/>, usuario técnico de integración).
/// </summary>
public sealed class SapTransferWriter : ISapTransferWriter
{
    private const string Resource = "StockTransfers";
    private const string DeliveryResource = "DeliveryNotes";
    private readonly ISapConnectionProvider _sap;

    public SapTransferWriter(ISapConnectionProvider sap) => _sap = sap;

    public Task WriteAsync(int docEntry, IReadOnlyDictionary<string, object?> fields, CancellationToken ct = default) =>
        PatchAsync(Resource, docEntry, fields, ct);

    public Task<IReadOnlyDictionary<string, object?>> ReadAsync(int docEntry, IReadOnlyList<string> fields, CancellationToken ct = default) =>
        ReadCoreAsync(Resource, "La transferencia", docEntry, fields, ct);

    public Task WriteDeliveryAsync(int docEntry, IReadOnlyDictionary<string, object?> fields, CancellationToken ct = default) =>
        PatchAsync(DeliveryResource, docEntry, fields, ct);

    public Task<IReadOnlyDictionary<string, object?>> ReadDeliveryAsync(int docEntry, IReadOnlyList<string> fields, CancellationToken ct = default) =>
        ReadCoreAsync(DeliveryResource, "La Entrega", docEntry, fields, ct);

    private async Task PatchAsync(string resource, int docEntry, IReadOnlyDictionary<string, object?> fields, CancellationToken ct)
    {
        var session = await _sap.GetConnectionAsync(ct);
        // DocEntry como int: Service Layer exige "StockTransfers(123)", no "StockTransfers('123')".
        await session.PatchAsync(resource, docEntry, new Dictionary<string, object?>(fields), ct);
    }

    private async Task<IReadOnlyDictionary<string, object?>> ReadCoreAsync(
        string resource, string label, int docEntry, IReadOnlyList<string> fields, CancellationToken ct)
    {
        var session = await _sap.GetConnectionAsync(ct);
        var doc = await session.GetAsync<Dictionary<string, JsonElement>>($"{resource}({docEntry.ToString(CultureInfo.InvariantCulture)})", ct: ct)
                  ?? throw new InvalidOperationException($"{label} {docEntry} no existe en SAP.");
        return fields.ToDictionary(f => f, f => doc.TryGetValue(f, out var v) ? ToValue(v) : null);
    }

    /// <summary>JSON de Service Layer a un valor que se pueda volver a enviar tal cual.</summary>
    private static object? ToValue(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.TryGetInt64(out var l) ? l : v.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
