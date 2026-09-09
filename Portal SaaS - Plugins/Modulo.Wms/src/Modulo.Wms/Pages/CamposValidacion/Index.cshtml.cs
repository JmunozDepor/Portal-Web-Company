using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Wms.Pages.CamposValidacion;

/// <summary>
/// Mantención de wms_validation_fields -- lado Bajada, espejo de /wms/mapeo-campos.
/// Qué campos, al cambiar de valor entre corridas, re-marcan una fila de staging
/// como Pendiente para que se re-envíe a Oracle WMS Cloud.
/// </summary>
public sealed class IndexModel : WmsPageModelBase
{
    private readonly IValidationFieldService _validationFields;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IValidationFieldService validationFields, ICurrentCompanyAccessor currentCompany)
    {
        _validationFields = validationFields;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<WmsValidationField> Campos { get; private set; } = Array.Empty<WmsValidationField>();

    public IReadOnlyList<string> TiposEntidad => IValidationFieldService.TiposEntidad;

    [BindProperty]
    public NuevoCampoInput Nuevo { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Campos = await _validationFields.ListAllAsync(_currentCompany.CompanyId, ct);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Campos = await _validationFields.ListAllAsync(_currentCompany.CompanyId, ct);
            return Page();
        }

        try
        {
            await _validationFields.CreateAsync(_currentCompany.CompanyId, Nuevo.TipoEntidad, Nuevo.FieldName, Nuevo.IsActive, ct);
            SuccessMessage = "Campo de validación creado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public Task<IActionResult> OnPostActivarAsync(long id, CancellationToken ct) => SetActiveAsync(id, true, ct);

    public Task<IActionResult> OnPostDesactivarAsync(long id, CancellationToken ct) => SetActiveAsync(id, false, ct);

    public async Task<IActionResult> OnPostEliminarAsync(long id, CancellationToken ct)
    {
        try
        {
            await _validationFields.DeleteAsync(id, _currentCompany.CompanyId, ct);
            SuccessMessage = "Campo de validación eliminado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    private async Task<IActionResult> SetActiveAsync(long id, bool activo, CancellationToken ct)
    {
        try
        {
            await _validationFields.SetActiveAsync(id, _currentCompany.CompanyId, activo, ct);
            SuccessMessage = "Campo de validación actualizado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public sealed class NuevoCampoInput
    {
        [Required]
        public string TipoEntidad { get; set; } = "Item";

        [Required]
        [StringLength(100)]
        [Display(Name = "Campo")]
        public string FieldName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}
