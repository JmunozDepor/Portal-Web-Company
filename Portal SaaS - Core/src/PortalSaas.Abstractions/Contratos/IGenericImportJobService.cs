using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Trabajos de Importación Genérica en segundo plano (2026-10-15) -- ver
/// GenericImportJob (Data). Todo acotado a la compañía activa; el archivo se guarda una
/// sola vez en el servidor y los pasos siguientes lo referencian por Id.
/// </summary>
public interface IGenericImportJobService
{
    /// <summary>Guarda el archivo como borrador (Status Draft) con los parámetros del asistente. Devuelve el Id del trabajo.</summary>
    Task<Guid> CreateDraftAsync(GenericImportParametersDto parameters, string fileName, byte[] content, CancellationToken ct = default);

    /// <summary>Actualiza los parámetros de un borrador (el usuario cambió formato/vendedor/etc. sin volver a subir el archivo).</summary>
    Task UpdateDraftParametersAsync(Guid jobId, GenericImportParametersDto parameters, CancellationToken ct = default);

    /// <summary>Contenido del archivo del trabajo -- null si no existe, es de otra compañía o ya se purgó.</summary>
    Task<byte[]?> GetFileAsync(Guid jobId, CancellationToken ct = default);

    Task<GenericImportJobDto?> GetAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Trabajos de la compañía activa, más recientes primero (sin borradores).</summary>
    Task<IReadOnlyList<GenericImportJobDto>> ListAsync(int take = 100, CancellationToken ct = default);

    /// <summary>
    /// Pasa el borrador a la cola con los parámetros finales y lo entrega al worker. Rechaza
    /// con InvalidOperationException si ya no es borrador o si hay otro trabajo en curso
    /// (en cola o ejecutándose) con el mismo archivo en la compañía.
    /// </summary>
    Task EnqueueAsync(Guid jobId, GenericImportParametersDto parameters, CancellationToken ct = default);

    /// <summary>Progreso para el polling de la pantalla: en vivo mientras corre, y desde la base cuando terminó (o tras un reinicio).</summary>
    Task<GenericImportProgressDto?> GetProgressAsync(Guid jobId, CancellationToken ct = default);
}

/// <summary>Cola en memoria de trabajos a procesar -- el worker la consume de a uno.</summary>
public interface IGenericImportJobQueue
{
    void Enqueue(Guid jobId);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct);
}
