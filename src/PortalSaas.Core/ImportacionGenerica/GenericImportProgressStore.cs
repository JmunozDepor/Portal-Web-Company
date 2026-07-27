using System.Collections.Concurrent;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>Store en memoria (singleton) del progreso de trabajos de IGenericImportService -- portado de ImportacionGenericaProgresoStore.</summary>
public sealed class GenericImportProgressStore : IGenericImportProgressStore
{
    private readonly ConcurrentDictionary<string, GenericImportProgressDto> _progress = new();

    public void Update(string jobId, GenericImportProgressDto progress) => _progress[jobId] = progress;

    public GenericImportProgressDto? Get(string jobId) => _progress.TryGetValue(jobId, out var value) ? value : null;
}
