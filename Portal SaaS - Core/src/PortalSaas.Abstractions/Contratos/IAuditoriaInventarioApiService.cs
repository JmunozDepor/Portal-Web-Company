using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Contrato que implementa Modulo.AuditoriaInventario (repo externo, cargado por
/// PluginManager) y que PortalSaas.Host resuelve por DI para exponer
/// AuditoriaInventarioInboundEndpoints -- mismo patrón que
/// IWmsInboundIngestionService/WmsInboundEndpoints: el Host no referencia el
/// ensamblado del plugin en tiempo de compilación, solo esta interfaz de
/// Abstractions, resuelta en runtime contra la implementación real cargada
/// dinámicamente.
///
/// Todo método recibe companyId explícito (nunca ICurrentCompanyAccessor) porque la
/// PWA no tiene sesión de portal -- el Host resuelve companyId antes de llamar acá
/// (login: por Company.Code del body; el resto: por el token opaco vía
/// ResolveTokenAsync).
/// </summary>
public interface IAuditoriaInventarioApiService
{
    Task<CaptureLoginResult?> LoginAsync(Guid companyId, string username, string password, CancellationToken ct = default);

    /// <summary>Null si el token no existe o expiró -- el llamador (Host) responde 401.</summary>
    Task<CaptureTokenInfo?> ResolveTokenAsync(string token, CancellationToken ct = default);

    Task<CaptureMaestroPage> GetProductosAsync(Guid companyId, long afterId, CancellationToken ct = default);

    /// <summary>Total real de productos en el servidor -- la PWA lo compara contra lo que tiene cacheado localmente para detectar un maestro sincronizado a medias (ver Mantenedor).</summary>
    Task<int> GetProductosCountAsync(Guid companyId, CancellationToken ct = default);

    Task<IReadOnlyList<CaptureSucursalDto>> GetSucursalesAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>Capturadores ACTIVOS de la compañía -- alimenta el desplegable de Usuario en el login de la PWA (sin token, se llama antes de loguearse).</summary>
    Task<IReadOnlyList<CaptureUsuarioDto>> GetUsuariosAsync(Guid companyId, CancellationToken ct = default);

    Task<IReadOnlyList<CaptureSectorDto>> GetSectoresAsync(Guid companyId, long? branchId, CancellationToken ct = default);

    /// <summary>Todos los formatos habilitados por defecto si la compañía nunca configuró esto (ver Pages/ConfiguracionCaptura, admin-only).</summary>
    Task<CaptureAjustesDto> GetAjustesCapturaAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>Tareas de conteo ACTIVAS creadas desde el portal -- la PWA ya no crea sesiones, se suma a una de estas (ver Pages/AvanceConteo, 2026-10-15).</summary>
    Task<IReadOnlyList<CaptureSesionAbiertaDto>> GetSesionesAbiertasAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Solo transiciona una sesión YA EXISTENTE (creada desde el portal) -- lanza
    /// InvalidOperationException si el Id no existe (ya no hay create-on-sync) o si
    /// la sesión ya está CLOSED (no se puede reabrir ni modificar).
    /// </summary>
    Task UpsertSesionAsync(Guid companyId, long captureUserId, CaptureSesionUpsert request, CancellationToken ct = default);

    /// <summary>Devuelve la cantidad de filas procesadas (alta + actualización) -- idempotente por Id generado en el cliente.</summary>
    Task<int> UploadCapturasAsync(Guid companyId, long captureUserId, IReadOnlyList<CaptureItemDto> capturas, CancellationToken ct = default);
}
