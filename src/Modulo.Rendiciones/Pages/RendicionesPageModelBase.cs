using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Modulo.Rendiciones.Pages;

/// <summary>
/// Infra compartida por las páginas de este módulo: mensajes de acción post-redirect y
/// normalización de errores. Duplicado del mismo patrón que otros plugins de esta
/// plataforma -- un plugin solo referencia Abstractions, nunca comparte código con
/// otro plugin. [Authorize] acá (no hay convención global en el Host, ver
/// Modulo.Ventas/DetailGenericSalesDocumentModelBase.cs para el mismo patrón) --
/// bug real encontrado en la verificación E2E: sin esto, un request anónimo llegaba
/// directo al handler y crasheaba con 500 en vez de redirigir a /Account/Login (la
/// resolución de RendicionesDbContext exige ICurrentUserContext.OrganizationId, que
/// no existe sin sesión).
/// </summary>
[Authorize]
public abstract class RendicionesPageModelBase : PageModel
{
    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    /// <summary>Advertencia no bloqueante (política de gasto superada sin ser bloqueante, posible duplicado) -- el guardado ya ocurrió, esto solo informa.</summary>
    [TempData]
    public string? WarningMessage { get; set; }

    protected static string GetErrorMessage(Exception ex) =>
        ex is InvalidOperationException ? ex.Message : $"No se pudo completar la operación: {ex.Message}";

    /// <summary>
    /// Los catálogos de SAP (ej. ICostCenterCatalogService) no están garantizados en
    /// todo ambiente -- un catálogo caído no debe tumbar la página completa, solo dejar
    /// ese selector puntual vacío.
    /// </summary>
    protected static async Task<IReadOnlyList<T>> LoadCatalogSafeAsync<T>(
        Func<Task<IReadOnlyList<T>>> load, ILogger logger, string catalogName)
    {
        try
        {
            return await load();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo cargar el catálogo {Catalog}", catalogName);
            return Array.Empty<T>();
        }
    }
}
