using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Pages;

/// <summary>
/// Infra compartida por las páginas de este módulo: mensajes post-redirect, normalización
/// de errores y gate de permisos por menú. [Authorize] obligatorio -- sin esto un request
/// anónimo llega al handler y crashea con 500 (la resolución de DespachosDbContext exige
/// una compañía activa en sesión) en vez de redirigir a login.
///
/// HasActionAsync exige el código de menú CALIFICADO con el módulo ("ModuleCode.Code"),
/// por eso se antepone "Despachos." al MenuCode de cada página (sin calificar, el
/// permiso nunca se encuentra y todo da Forbid).
/// </summary>
[Authorize]
public abstract class DespachosPageModelBase : PageModel
{
    private readonly ICurrentUserContext _currentUser;

    protected DespachosPageModelBase(ICurrentUserContext currentUser)
    {
        _currentUser = currentUser;
    }

    /// <summary>Código de menú de la hoja (sin calificar), ej. "manifiestos".</summary>
    protected abstract string MenuCode { get; }

    protected Task<bool> HasActionAsync(string action) =>
        _currentUser.HasActionAsync("Despachos." + MenuCode, action);

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    /// <summary>Advertencia no bloqueante: la operación ya ocurrió, esto solo informa.</summary>
    [TempData]
    public string? WarningMessage { get; set; }

    protected static string GetErrorMessage(Exception ex) =>
        ex is InvalidOperationException ? ex.Message : $"No se pudo completar la operación: {ex.Message}";
}
