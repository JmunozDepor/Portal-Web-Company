using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PortalSaas.Host.Pages;

/// <summary>
/// Página de error única para toda la app -- destino de app.UseExceptionHandler("/Error")
/// en Program.cs (activo en TODOS los entornos, no solo producción; antes solo corría
/// fuera de Development, así que cualquier excepción no manejada en local mostraba la
/// pantalla cruda de diagnóstico de ASP.NET Core en vez de esto). Nunca muestra mensaje
/// de excepción/stack trace fuera de Development -- puede filtrar detalles internos
/// (connection strings, rutas de archivo, nombres de tabla) a un usuario real.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    private readonly ILogger<ErrorModel> _logger;
    private readonly IWebHostEnvironment _environment;

    public ErrorModel(ILogger<ErrorModel> logger, IWebHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public string RequestId { get; private set; } = string.Empty;
    public bool ShowDetails { get; private set; }
    public string? ExceptionType { get; private set; }
    public string? ExceptionMessage { get; private set; }
    public string? StackTrace { get; private set; }
    public string? RoutePath { get; private set; }

    public void OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (feature is null)
        {
            return;
        }

        RoutePath = feature.Path;

        // Loguear SIEMPRE (para poder diagnosticar un incidente real de producción a
        // partir del RequestId que sí se muestra al usuario), mostrar detalle solo en
        // Development.
        _logger.LogError(feature.Error, "Excepción no manejada en {Path} (RequestId {RequestId}).", feature.Path, RequestId);

        ShowDetails = _environment.IsDevelopment();
        if (ShowDetails)
        {
            ExceptionType = feature.Error.GetType().FullName;
            ExceptionMessage = feature.Error.Message;
            StackTrace = feature.Error.ToString();
        }
    }
}
