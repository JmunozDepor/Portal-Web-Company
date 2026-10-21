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

    /// <summary>Trabajos de la compañía activa, más recientes primero: los últimos <paramref name="take"/> enviados + todas las cargas guardadas (sin borradores temporales).</summary>
    Task<IReadOnlyList<GenericImportJobDto>> ListAsync(int take = 100, CancellationToken ct = default);

    /// <summary>
    /// Pasa el borrador a la cola y lo entrega al worker. Un borrador temporal toma los
    /// parámetros finales recibidos; una carga GUARDADA se envía con los suyos (sin
    /// edición) y la puede enviar cualquier usuario con permiso. Rechaza con
    /// InvalidOperationException si ya se envió, si hay otro trabajo en curso con el mismo
    /// archivo o si el mismo archivo está guardado en otro trabajo.
    /// </summary>
    Task EnqueueAsync(Guid jobId, GenericImportParametersDto parameters, CancellationToken ct = default);

    /// <summary>
    /// "Guardar sin enviar" (borradores etapa 2): el borrador temporal pasa a Guardado con
    /// sus parámetros finales y el motivo (obligatorio). Ya no lo borra la limpieza de
    /// borradores temporales; vence según la retención de la compañía. Rechaza si no es un
    /// borrador temporal o si el mismo archivo ya está guardado o en curso en otro trabajo.
    /// </summary>
    Task SaveAsync(Guid jobId, GenericImportParametersDto parameters, string reason, CancellationToken ct = default);

    /// <summary>Descarta (elimina) una carga guardada sin enviar. Avisa al creador si la descarta otro usuario.</summary>
    Task DiscardAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Progreso para el polling de la pantalla: en vivo mientras corre, y desde la base cuando terminó (o tras un reinicio).</summary>
    Task<GenericImportProgressDto?> GetProgressAsync(Guid jobId, CancellationToken ct = default);
}

/// <summary>Cola en memoria de trabajos a procesar -- el worker la consume de a uno.</summary>
public interface IGenericImportJobQueue
{
    void Enqueue(Guid jobId);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken ct);
}
