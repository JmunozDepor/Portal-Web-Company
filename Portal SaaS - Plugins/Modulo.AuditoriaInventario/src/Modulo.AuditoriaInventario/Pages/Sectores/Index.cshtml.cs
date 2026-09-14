using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Sectores;

/// <summary>Mantenedor de InventorySector -- catálogo simple, sin lógica de negocio calculada.</summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    [BindProperty]
    public SectorInput Input { get; set; } = new();

    public IReadOnlyList<SectorRowDto> Sectores { get; private set; } = Array.Empty<SectorRowDto>();
    public List<SelectListItem> SucursalesDisponibles { get; private set; } = new();
    public long? Editando { get; set; }

    public async Task OnGetAsync(long? editando, CancellationToken ct)
    {
        await CargarListasAsync(ct);

        if (editando is { } id)
        {
            var sector = await _db.InventorySectors.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == _currentCompany.CompanyId, ct);
            if (sector is not null)
            {
                Editando = id;
                Input = new SectorInput { Name = sector.Name, BranchId = sector.BranchId, IsActive = sector.IsActive };
            }
        }
    }

    public async Task<IActionResult> OnPostGuardarAsync(long? editando, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Editando = editando;
            await CargarListasAsync(ct);
            return Page();
        }

        try
        {
            if (editando is { } id)
            {
                var sector = await _db.InventorySectors.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == _currentCompany.CompanyId, ct)
                    ?? throw new InvalidOperationException("El sector no existe.");
                AplicarInput(sector);
                SuccessMessage = "Sector actualizado.";
            }
            else
            {
                var sector = new InventorySector { CompanyId = _currentCompany.CompanyId, Name = Input.Name };
                AplicarInput(sector);
                _db.InventorySectors.Add(sector);
                SuccessMessage = "Sector creado.";
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            Editando = editando;
            await CargarListasAsync(ct);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEliminarAsync(long id, CancellationToken ct)
    {
        try
        {
            var sector = await _db.InventorySectors.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == _currentCompany.CompanyId, ct)
                ?? throw new InvalidOperationException("El sector no existe.");
            _db.InventorySectors.Remove(sector);
            await _db.SaveChangesAsync(ct);
            SuccessMessage = "Sector eliminado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    private void AplicarInput(InventorySector sector)
    {
        sector.Name = Input.Name;
        sector.BranchId = Input.BranchId;
        sector.IsActive = Input.IsActive;
    }

    private async Task CargarListasAsync(CancellationToken ct)
    {
        var sucursales = await _db.Branches
            .Where(b => b.CompanyId == _currentCompany.CompanyId)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);
        SucursalesDisponibles = sucursales.Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToList();
        var nombreSucursal = sucursales.ToDictionary(b => b.Id, b => b.Name);

        var sectores = await _db.InventorySectors
            .Where(s => s.CompanyId == _currentCompany.CompanyId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        Sectores = sectores.Select(s => new SectorRowDto(
            s.Id, s.Name, s.BranchId is { } bid ? nombreSucursal.GetValueOrDefault(bid, "?") : "Todas", s.IsActive)).ToList();
    }

    public sealed class SectorInput
    {
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public long? BranchId { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public sealed record SectorRowDto(long Id, string Name, string BranchName, bool IsActive);
}
