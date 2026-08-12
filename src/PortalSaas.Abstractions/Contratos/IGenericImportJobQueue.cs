namespace PortalSaas.Abstractions.Contratos;

using PortalSaas.Abstractions.Modelos;

/// <summary>
/// Cola en memoria de proceso para desacoplar la creación de documentos de
/// importación del hilo de request HTTP -- ver docs/superpowers/plans Task 7 para el
/// límite reconocido (no durable entre reciclajes de proceso).
/// </summary>
public interface IGenericImportJobQueue
{
    void Enqueue(GenericImportJobRequest request);
    ValueTask<GenericImportJobRequest> DequeueAsync(CancellationToken ct);
}
