using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.FacturasProveedor.Pages;

/// <summary>
/// Infra compartida por las páginas del módulo: mensajes post-redirect, normalización de errores y gate de permisos por
/// menú (mismo patrón que DespachosPageModelBase). [Authorize] obligatorio: sin compañía en sesión la resolución del
/// DbContext falla, y un anónimo debe ir al login, no a un 500.
///
/// HasActionAsync exige el código de menú CALIFICADO con el módulo ("FacturasProveedor.&lt;código&gt;").
/// </summary>
[Authorize]
public abstract class FacturasProveedorPageModelBase : PageModel
{
    private readonly ICurrentUserContext _currentUser;

    protected FacturasProveedorPageModelBase(ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
    }

    /// <summary>Código de menú de la hoja (sin calificar), ej. "bandeja".</summary>
    protected abstract string MenuCode { get; }

    protected ICurrentUserContext CurrentUser => _currentUser;

    protected Task<bool> HasActionAsync(string action) =>
        _currentUser.HasActionAsync("FacturasProveedor." + MenuCode, action);

    /// <summary>Permiso sobre OTRO menú del módulo (ej. decidir cualquier factura exige EDIT en "bandeja").</summary>
    protected Task<bool> HasActionOnAsync(string menuCode, string action) =>
        _currentUser.HasActionAsync("FacturasProveedor." + menuCode, action);

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    /// <summary>Advertencia no bloqueante: la operación ya ocurrió, esto solo informa.</summary>
    [TempData]
    public string? WarningMessage { get; set; }

    protected static string GetErrorMessage(Exception ex) => ex switch
    {
        InvalidOperationException => ex.Message,
        Microsoft.EntityFrameworkCore.DbUpdateException =>
            "La factura cambió mientras se guardaba (otro proceso la estaba procesando). Recargue la página y vuelva a intentar.",
        _ => $"No se pudo completar la operación: {ex.Message}",
    };
}
