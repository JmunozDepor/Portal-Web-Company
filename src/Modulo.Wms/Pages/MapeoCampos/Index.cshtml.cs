using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Wms.Pages.MapeoCampos;

public sealed class IndexModel : WmsPageModelBase
{
    private readonly IFieldMappingService _fieldMappings;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ICurrentUserContext _currentUser;

    public IndexModel(IFieldMappingService fieldMappings, ICurrentCompanyAccessor currentCompany, ICurrentUserContext currentUser)
    {
        _fieldMappings = fieldMappings;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
    }

    public IReadOnlyList<WmsFieldMapping> Mappings { get; private set; } = Array.Empty<WmsFieldMapping>();

    public IReadOnlyDictionary<string, string> MapperKeyLabels => WmsFieldMapperKeys.Labels;

    [BindProperty]
    public NewMappingInput New { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Mappings = await _fieldMappings.ListAllAsync(_currentCompany.CompanyId, ct);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Mappings = await _fieldMappings.ListAllAsync(_currentCompany.CompanyId, ct);
            return Page();
        }

        try
        {
            await _fieldMappings.CreateAsync(_currentCompany.CompanyId, New.MapperKey, New.FieldName, New.ValueTemplate, New.IsActive, _currentUser.Username, ct);
            SuccessMessage = "Mapeo creado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGuardarAsync(long id, string valueTemplate, bool activo, CancellationToken ct) =>
        await UpdateAsync(id, valueTemplate, activo, ct);

    public async Task<IActionResult> OnPostDesactivarAsync(long id, string valueTemplate, CancellationToken ct) =>
        await UpdateAsync(id, valueTemplate, activo: false, ct);

    public async Task<IActionResult> OnPostActivarAsync(long id, string valueTemplate, CancellationToken ct) =>
        await UpdateAsync(id, valueTemplate, activo: true, ct);

    private async Task<IActionResult> UpdateAsync(long id, string valueTemplate, bool activo, CancellationToken ct)
    {
        try
        {
            await _fieldMappings.UpdateAsync(id, _currentCompany.CompanyId, valueTemplate, activo, _currentUser.Username, ct);
            SuccessMessage = "Mapeo actualizado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public sealed class NewMappingInput
    {
        [Required]
        public string MapperKey { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string FieldName { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string ValueTemplate { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
    }
}
