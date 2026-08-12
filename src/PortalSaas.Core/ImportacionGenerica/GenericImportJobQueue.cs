using System.Threading.Channels;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>Ver IGenericImportJobQueue. Singleton -- un solo canal compartido por toda la instancia del proceso.</summary>
public sealed class GenericImportJobQueue : IGenericImportJobQueue
{
    private readonly Channel<GenericImportJobRequest> _channel = Channel.CreateUnbounded<GenericImportJobRequest>();

    public void Enqueue(GenericImportJobRequest request) => _channel.Writer.TryWrite(request);

    public ValueTask<GenericImportJobRequest> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}
