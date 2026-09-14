using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;

namespace PortalSaas.Host.AuditoriaInventario;

/// <summary>
/// Endpoints HTTP consumidos por la PWA de captura de inventario (repo separado,
/// equipo mobile) -- mismo patrón que WmsInboundEndpoints: el Host mapea las rutas
/// acá mismo (NO como MVC Controllers dentro del plugin, ese enfoque no funciona
/// porque el Host solo llama AddRazorPages(), nunca AddControllers()/
/// MapControllers() -- ver AssemblyPart en PluginManager.LoadModule, que sí
/// descubre Razor Pages pero no activa el pipeline de MVC controllers) y delega en
/// IAuditoriaInventarioApiService (Abstractions), resuelta por DI contra la
/// implementación real que carga PluginManager desde Modulo.AuditoriaInventario.
///
/// Autenticación: NO usa el esquema "ExternalApiKey" (pensado para integraciones
/// servidor-a-servidor con una key por organización, no para usuarios individuales
/// de campo) -- la PWA loguea con credencial propia (CaptureUser) y recibe un token
/// opaco; estos endpoints validan ese token a mano en cada request vía
/// ResolveTokenAsync, sin integrar el pipeline de autenticación de ASP.NET Core.
/// </summary>
public static class AuditoriaInventarioInboundEndpoints
{
    private const string RoutePrefix = "/api/auditoria-inventario/v1";

    public static void MapAuditoriaInventarioInboundEndpoints(this WebApplication app)
    {
        app.MapPost($"{RoutePrefix}/auth/login", LoginAsync);
        app.MapGet($"{RoutePrefix}/maestro/productos", GetProductosAsync);
        app.MapGet($"{RoutePrefix}/maestro/sucursales", GetSucursalesAsync);
        app.MapGet($"{RoutePrefix}/maestro/sectores", GetSectoresAsync);
        app.MapPost($"{RoutePrefix}/sesiones", UpsertSesionAsync);
        app.MapPost($"{RoutePrefix}/capturas/batch", UploadCapturasBatchAsync);
    }

    private sealed record LoginRequest(string CompanyCode, string Username, string Password);

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        PortalSaasDbContext portalDb,
        IAuditoriaInventarioApiService api,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyCode) || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest("CompanyCode, Username y Password son obligatorios.");
        }

        var company = await portalDb.Companies.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == request.CompanyCode && c.IsActive, ct);
        if (company is null)
        {
            return Results.Unauthorized();
        }

        var result = await api.LoginAsync(company.Id, request.Username, request.Password, ct);
        return result is null ? Results.Unauthorized() : Results.Ok(result);
    }

    /// <summary>Resuelve el token del header Authorization: Bearer {token}. Null si falta o el esquema no es Bearer.</summary>
    private static async Task<CaptureTokenInfo?> ResolveAuthAsync(HttpContext httpContext, IAuditoriaInventarioApiService api, CancellationToken ct)
    {
        var header = httpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await api.ResolveTokenAsync(header["Bearer ".Length..].Trim(), ct);
    }

    private static async Task<IResult> GetProductosAsync(
        long afterId, HttpContext httpContext, IAuditoriaInventarioApiService api, CancellationToken ct)
    {
        var auth = await ResolveAuthAsync(httpContext, api, ct);
        if (auth is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await api.GetProductosAsync(auth.CompanyId, afterId, ct));
    }

    private static async Task<IResult> GetSucursalesAsync(HttpContext httpContext, IAuditoriaInventarioApiService api, CancellationToken ct)
    {
        var auth = await ResolveAuthAsync(httpContext, api, ct);
        if (auth is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await api.GetSucursalesAsync(auth.CompanyId, ct));
    }

    private static async Task<IResult> GetSectoresAsync(
        long? branchId, HttpContext httpContext, IAuditoriaInventarioApiService api, CancellationToken ct)
    {
        var auth = await ResolveAuthAsync(httpContext, api, ct);
        if (auth is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await api.GetSectoresAsync(auth.CompanyId, branchId, ct));
    }

    private static async Task<IResult> UpsertSesionAsync(
        CaptureSesionUpsert request, HttpContext httpContext, IAuditoriaInventarioApiService api, CancellationToken ct)
    {
        var auth = await ResolveAuthAsync(httpContext, api, ct);
        if (auth is null)
        {
            return Results.Unauthorized();
        }

        await api.UpsertSesionAsync(auth.CompanyId, auth.CaptureUserId, request, ct);
        return Results.Ok();
    }

    private static async Task<IResult> UploadCapturasBatchAsync(
        List<CaptureItemDto> capturas, HttpContext httpContext, IAuditoriaInventarioApiService api, CancellationToken ct)
    {
        var auth = await ResolveAuthAsync(httpContext, api, ct);
        if (auth is null)
        {
            return Results.Unauthorized();
        }

        var procesadas = await api.UploadCapturasAsync(auth.CompanyId, auth.CaptureUserId, capturas, ct);
        return Results.Ok(new { processed = procesadas });
    }
}
