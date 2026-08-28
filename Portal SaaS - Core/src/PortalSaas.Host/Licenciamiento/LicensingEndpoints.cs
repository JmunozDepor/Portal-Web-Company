using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Host.Licenciamiento;

/// <summary>
/// Únicos endpoints HTTP entrantes del Host que no son Razor Pages -- mapeados desde
/// Program.cs solo cuando Licensing:Role = "Central" (ver plan). No amerita introducir
/// MVC Controllers para dos rutas puntuales; Minimal API alcanza. Activación y
/// heartbeat son la MISMA operación del lado central (ILicenseActivationService no
/// distingue "primera vez" de "siguiente vez", ver su doc-comment) -- por eso ambas
/// rutas mapean al mismo handler.
/// </summary>
public static class LicensingEndpoints
{
    public static void MapLicensingEndpoints(this WebApplication app)
    {
        app.MapPost("/api/licensing/activate", HandleAsync);
        app.MapPost("/api/licensing/heartbeat", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        LicensingRequest request,
        ILicenseActivationService activationService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ActivationKey) || string.IsNullOrWhiteSpace(request.Fingerprint))
        {
            return Results.BadRequest(new { Reason = "ActivationKey y Fingerprint son obligatorios." });
        }

        var sourceIp = httpContext.Connection.RemoteIpAddress?.ToString();
        var result = await activationService.ActivateOrHeartbeatAsync(request.ActivationKey, request.Fingerprint, sourceIp, ct);

        return Results.Ok(new { result.IsAccepted, result.SignedToken, result.Reason });
    }

    private sealed record LicensingRequest(string ActivationKey, string Fingerprint);
}
