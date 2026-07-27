using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.ImportacionGenerica.Pages.CamposUsuario;

/// <summary>
/// Catálogo maestro de campos de usuario (UDF dinámicos) de la organización -- solo
/// administrador. Portado de Pages/CamposUsuario/Index.cshtml.cs (referencia-original/
/// PortalSAP_v2), CRUD acotado a la organización actual (ver
/// IGenericImportUserFieldService). Edición inline vía ?editId= (sin página Edit
/// separada) -- mismo criterio que Admin/Profiles/Edit de este proyecto.
/// </summary>
public sealed class IndexModel : PageModelBaseAdmin
{
    private readonly IGenericImportUserFieldService _userFields;

    public IndexModel(ICurrentUserContext currentUser, IGenericImportUserFieldService userFields) : base(currentUser)
    {
        _userFields = userFields;
    }

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<GenericImportUserFieldDto> Fields { get; private set; } = [];

    public List<SelectListItem> Modules { get; } =
    [
        new(GenericImportModule.Sales.ToString(), GenericImportModule.Sales.ToString()),
        new(GenericImportModule.Purchase.ToString(), GenericImportModule.Purchase.ToString()),
        new(GenericImportModule.Inventory.ToString(), GenericImportModule.Inventory.ToString()),
    ];

    public List<SelectListItem> Levels { get; } =
    [
        new("Cabecera", GenericImportFieldLevel.Header.ToString()),
        new("Línea", GenericImportFieldLevel.Line.ToString()),
    ];

    public List<SelectListItem> DataTypes { get; } =
    [
        new("Texto", GenericImportFieldDataType.Text.ToString()),
        new("Número", GenericImportFieldDataType.Number.ToString()),
        new("Fecha", GenericImportFieldDataType.Date.ToString()),
    ];

    public async Task OnGetAsync(CancellationToken ct)
    {
        Fields = await _userFields.ListAsync(ct: ct);

        if (EditId is { } id)
        {
            var existing = Fields.FirstOrDefault(f => f.Id == id);
            if (existing is not null)
            {
                Input = new InputModel
                {
                    Module = existing.Module,
                    Level = existing.Level,
                    Label = existing.Label,
                    SapFieldName = existing.SapFieldName,
                    DataType = existing.DataType,
                    IsActive = existing.IsActive,
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Fields = await _userFields.ListAsync(ct: ct);
            return Page();
        }

        try
        {
            if (EditId is { } id)
            {
                await _userFields.UpdateAsync(id, Input.Label, Input.SapFieldName, Input.DataType, Input.IsActive, ct);
                SuccessMessage = "Campo de usuario actualizado.";
            }
            else
            {
                await _userFields.CreateAsync(Input.Module, Input.Level, Input.Label, Input.SapFieldName, Input.DataType, ct);
                SuccessMessage = "Campo de usuario creado.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        await _userFields.DeleteAsync(id, ct);
        SuccessMessage = "Campo de usuario eliminado.";
        return RedirectToPage();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Elegí un módulo.")]
        [Display(Name = "Módulo")]
        public GenericImportModule Module { get; set; }

        [Required(ErrorMessage = "Elegí un nivel.")]
        [Display(Name = "Nivel")]
        public GenericImportFieldLevel Level { get; set; }

        [Required(ErrorMessage = "La etiqueta es obligatoria.")]
        [Display(Name = "Etiqueta")]
        public string Label { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre de campo en Service Layer es obligatorio.")]
        [Display(Name = "Nombre de campo (Service Layer)")]
        public string SapFieldName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Elegí un tipo de dato.")]
        [Display(Name = "Tipo de dato")]
        public GenericImportFieldDataType DataType { get; set; }

        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;
    }
}
