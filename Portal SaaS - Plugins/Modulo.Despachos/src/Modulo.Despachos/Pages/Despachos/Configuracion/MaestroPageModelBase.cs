using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Despachos.Pages.Despachos.Configuracion;

/// <summary>
/// Molde de las pantallas de mantenedores: cada handler valida el permiso del menú de la
/// página (VIEW para ver, CREATE/EDIT para guardar según Id, DELETE para eliminar) y
/// responde Forbid() si falta. La edición y el alta comparten un drawer; el JS
/// (wwwroot/js/despachos-maestros.js) lo rellena con el JSON de la fila (RowJson).
/// </summary>
public abstract class MaestroPageModelBase : DespachosPageModelBase
{
    private static readonly JsonSerializerOptions RowJsonOptions = new() { PropertyNamingPolicy = null };

    protected MaestroPageModelBase(ICurrentUserContext currentUser) : base(currentUser) { }

    public bool CanCreate { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanDelete { get; private set; }

    /// <summary>Serializa la fila para el atributo data-row del botón Editar (claves = nombres de Input.*).</summary>
    public string RowJson(object row) => JsonSerializer.Serialize(row, RowJsonOptions);

    /// <summary>GET: exige VIEW, calcula los permisos de los botones y carga los datos.</summary>
    protected async Task<IActionResult> LoadAsync(Func<Task> load)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        CanCreate = await HasActionAsync(PortalActions.Create);
        CanEdit = await HasActionAsync(PortalActions.Edit);
        CanDelete = await HasActionAsync(PortalActions.Delete);
        await load();
        return Page();
    }

    /// <summary>Guardar: CREATE si id == 0, si no EDIT. Errores de negocio vuelven como ErrorMessage.</summary>
    protected async Task<IActionResult> SaveCoreAsync(long id, Func<Task> save, string okMessage)
    {
        if (!await HasActionAsync(id == 0 ? PortalActions.Create : PortalActions.Edit)) return Forbid();
        try
        {
            await save();
            SuccessMessage = okMessage;
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }
        return RedirectToPage();
    }

    /// <summary>Eliminar: exige DELETE; si hay dependientes el servicio explica por qué no se pudo.</summary>
    protected async Task<IActionResult> DeleteCoreAsync(Func<Task<DeleteResult>> delete, string okMessage)
    {
        if (!await HasActionAsync(PortalActions.Delete)) return Forbid();
        try
        {
            var result = await delete();
            if (result.Deleted) SuccessMessage = okMessage;
            else ErrorMessage = result.BlockedReason ?? "No se pudo eliminar el registro.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }
        return RedirectToPage();
    }

    /// <summary>
    /// Los decimales se reciben como texto y se parsean en cultura invariante: el Host fija
    /// es-CL y el binder interpretaría "12.5" (lo que envía un input number) como 125.
    /// </summary>
    protected static decimal? ParseDecimal(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = text.Trim().Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"{label} no es un número válido.");
        return value;
    }

    protected static DateOnly? ParseDate(string? text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new InvalidOperationException($"{label} no es una fecha válida.");
        return value;
    }

    protected static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
