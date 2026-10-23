using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Fakes;

/// <summary>Escritor SAP de prueba: registra cada escritura; <see cref="FailWith"/> simula un rechazo de SAP.</summary>
public sealed class FakeSapTransferWriter : ISapTransferWriter
{
    public List<(int DocEntry, IReadOnlyDictionary<string, object?> Fields)> Writes { get; } = new();

    /// <summary>Valores actuales por DocEntry que devuelve ReadAsync.</summary>
    public Dictionary<int, Dictionary<string, object?>> Current { get; } = new();

    public string? FailWith { get; set; }

    public Task WriteAsync(int docEntry, IReadOnlyDictionary<string, object?> fields, CancellationToken ct = default)
    {
        if (FailWith is not null) throw new InvalidOperationException(FailWith);
        Writes.Add((docEntry, fields));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, object?>> ReadAsync(int docEntry, IReadOnlyList<string> fields, CancellationToken ct = default)
    {
        var current = Current.TryGetValue(docEntry, out var c) ? c : new Dictionary<string, object?>();
        IReadOnlyDictionary<string, object?> result = fields.ToDictionary(f => f, f => current.TryGetValue(f, out var v) ? v : null);
        return Task.FromResult(result);
    }
}
