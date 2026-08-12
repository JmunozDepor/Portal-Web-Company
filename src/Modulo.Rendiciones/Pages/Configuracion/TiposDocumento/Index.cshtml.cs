using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Pages.Configuracion.TiposDocumento;

public sealed class IndexModel : RendicionesPageModelBase
{
    private readonly IDocumentTypeService _documentTypes;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(IDocumentTypeService documentTypes, ICurrentCompanyAccessor currentCompany)
    {
        _documentTypes = documentTypes;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<DocumentType> Types { get; private set; } = Array.Empty<DocumentType>();

    [BindProperty]
    public NewDocumentTypeInput New { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Types = await _documentTypes.ListAllAsync(_currentCompany.CompanyId, ct);
    }

    public async Task<IActionResult> OnPostCrearAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Types = await _documentTypes.ListAllAsync(_currentCompany.CompanyId, ct);
            return Page();
        }

        await _documentTypes.CreateAsync(_currentCompany.CompanyId, New.Name, New.AppliesTax, New.TaxPercentage, ct);
        SuccessMessage = "Tipo de documento creado.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostGuardarAsync(long id, string nombre, bool aplicaIva, decimal porcentajeIva, bool activo, CancellationToken ct)
    {
        try
        {
            await _documentTypes.UpdateAsync(id, _currentCompany.CompanyId, nombre, aplicaIva, porcentajeIva, activo, ct);
            SuccessMessage = "Tipo de documento actualizado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDesactivarAsync(long id, string nombre, bool aplicaIva, decimal porcentajeIva, CancellationToken ct) =>
        await OnPostGuardarAsync(id, nombre, aplicaIva, porcentajeIva, activo: false, ct);

    public async Task<IActionResult> OnPostActivarAsync(long id, string nombre, bool aplicaIva, decimal porcentajeIva, CancellationToken ct) =>
        await OnPostGuardarAsync(id, nombre, aplicaIva, porcentajeIva, activo: true, ct);

    public sealed class NewDocumentTypeInput
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool AppliesTax { get; set; } = true;

        [Range(0, 100)]
        public decimal TaxPercentage { get; set; } = 19.00m;
    }
}
