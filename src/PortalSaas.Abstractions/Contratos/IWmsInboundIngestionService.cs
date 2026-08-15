namespace PortalSaas.Abstractions.Contratos;

public interface IWmsInboundIngestionService
{
    Task<WmsInboundIngestionResult> InsertPendingAsync(
        Guid companyId,
        string tipoDoc,
        string formato,
        string nombreArchivo,
        string hashArchivo,
        string contenido,
        CancellationToken cancellationToken);
}

public sealed class WmsInboundIngestionResult
{
    public required bool Insertado { get; init; }
    public required bool Duplicado { get; init; }
}
