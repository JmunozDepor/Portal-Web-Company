using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Sucursales;

/// <summary>Mantenedor de Branch -- catálogo simple, sin lógica de negocio calculada.</summary>
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
    public SucursalInput Input { get; set; } = new();

    public IReadOnlyList<Branch> Sucursales { get; private set; } = Array.Empty<Branch>();
    public long? Editando { get; set; }

    public async Task OnGetAsync(long? editando, CancellationToken ct)
    {
        await CargarListaAsync(ct);

        if (editando is { } id)
        {
            var sucursal = await _db.Branches.FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == _currentCompany.CompanyId, ct);
            if (sucursal is not null)
            {
                Editando = id;
                Input = new SucursalInput
                {
                    BranchCode = sucursal.BranchCode,
                    Name = sucursal.Name,
                    IsActive = sucursal.IsActive,
                    SapCompanyCode = sucursal.SapCompanyCode,
                    SapWarehouseCode = sucursal.SapWarehouseCode,
                };
            }
        }
    }

    public async Task<IActionResult> OnPostGuardarAsync(long? editando, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Editando = editando;
            await CargarListaAsync(ct);
            return Page();
        }

        try
        {
            if (editando is { } id)
            {
                var sucursal = await _db.Branches.FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == _currentCompany.CompanyId, ct)
                    ?? throw new InvalidOperationException("La sucursal no existe.");
                AplicarInput(sucursal);
                SuccessMessage = "Sucursal actualizada.";
            }
            else
            {
                if (await _db.Branches.AnyAsync(b => b.CompanyId == _currentCompany.CompanyId && b.BranchCode == Input.BranchCode, ct))
                {
                    throw new InvalidOperationException($"Ya existe una sucursal con el código '{Input.BranchCode}'.");
                }

                var sucursal = new Branch { CompanyId = _currentCompany.CompanyId, BranchCode = Input.BranchCode, Name = Input.Name };
                AplicarInput(sucursal);
                _db.Branches.Add(sucursal);
                SuccessMessage = "Sucursal creada.";
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            Editando = editando;
            await CargarListaAsync(ct);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEliminarAsync(long id, CancellationToken ct)
    {
        try
        {
            var sucursal = await _db.Branches.FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == _currentCompany.CompanyId, ct)
                ?? throw new InvalidOperationException("La sucursal no existe.");
            _db.Branches.Remove(sucursal);
            await _db.SaveChangesAsync(ct);
            SuccessMessage = "Sucursal eliminada.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    private void AplicarInput(Branch sucursal)
    {
        sucursal.BranchCode = Input.BranchCode;
        sucursal.Name = Input.Name;
        sucursal.IsActive = Input.IsActive;
        sucursal.SapCompanyCode = string.IsNullOrWhiteSpace(Input.SapCompanyCode) ? null : Input.SapCompanyCode;
        sucursal.SapWarehouseCode = string.IsNullOrWhiteSpace(Input.SapWarehouseCode) ? null : Input.SapWarehouseCode;
    }

    private async Task CargarListaAsync(CancellationToken ct)
    {
        Sucursales = await _db.Branches
            .Where(b => b.CompanyId == _currentCompany.CompanyId)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);
    }

    public sealed class SucursalInput
    {
        [Required(ErrorMessage = "El código es obligatorio.")]
        [MaxLength(20)]
        public string BranchCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        [MaxLength(20)]
        public string? SapCompanyCode { get; set; }

        [MaxLength(20)]
        public string? SapWarehouseCode { get; set; }
    }
}
