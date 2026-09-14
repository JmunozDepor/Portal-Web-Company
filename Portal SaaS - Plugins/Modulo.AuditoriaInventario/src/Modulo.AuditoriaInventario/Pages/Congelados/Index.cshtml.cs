using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Congelados;

/// <summary>
/// Listado de congelados cargados + formulario de carga (Excel exportado del punto
/// de venta contra un Nro. de Inventario) -- ver CongeladoExcelParser para el
/// mapeo de columnas.
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ICurrentUserContext _currentUser;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
    }

    [BindProperty]
    public CargaCongeladoInput Input { get; set; } = new();

    public IReadOnlyList<CongeladoRowDto> Congelados { get; private set; } = Array.Empty<CongeladoRowDto>();
    public IReadOnlyList<Branch> SucursalesActivas { get; private set; } = Array.Empty<Branch>();
    public IReadOnlyList<string> ErroresDeCarga { get; private set; } = Array.Empty<string>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        await CargarCongeladosAsync(ct);
        await CargarSucursalesAsync(ct);
    }

    public async Task<IActionResult> OnPostCargarAsync(CancellationToken ct)
    {
        await CargarCongeladosAsync(ct);
        await CargarSucursalesAsync(ct);

        if (Input.Archivo is null || Input.Archivo.Length == 0)
        {
            ErrorMessage = "Seleccioná un archivo Excel para cargar.";
            return Page();
        }

        if (Input.BranchId is null || string.IsNullOrWhiteSpace(Input.InventoryNumber))
        {
            ErrorMessage = "Seleccioná la sucursal e ingresá el Nro. de Inventario.";
            return Page();
        }

        CongeladoParseResult resultado;
        await using (var stream = Input.Archivo.OpenReadStream())
        {
            resultado = CongeladoExcelParser.Parse(stream);
        }

        if (resultado.Errores.Count > 0)
        {
            ErroresDeCarga = resultado.Errores.Select(e => $"Fila {e.NumeroFila}: {e.Mensaje}").ToList();
            ErrorMessage = $"El archivo tiene {resultado.Errores.Count} fila(s) con errores -- corregilas y volvé a subirlo.";
            return Page();
        }

        if (resultado.Filas.Count == 0)
        {
            ErrorMessage = "El archivo no tiene filas para cargar.";
            return Page();
        }

        var snapshot = new FrozenInventorySnapshot
        {
            CompanyId = _currentCompany.CompanyId,
            BranchId = Input.BranchId.Value,
            InventoryNumber = Input.InventoryNumber,
            LoadedByUserId = _currentUser.UserId,
            FileName = Input.Archivo.FileName,
        };
        _db.FrozenInventorySnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);

        _db.FrozenInventoryLines.AddRange(resultado.Filas.Select(f => new FrozenInventoryLine
        {
            SnapshotId = snapshot.Id,
            Barcode = f.Barcode,
            ProductCode = f.ProductCode,
            Quantity = f.Quantity,
            UnitCost = f.UnitCost,
        }));
        await _db.SaveChangesAsync(ct);

        SuccessMessage = $"Congelado cargado: {resultado.Filas.Count} línea(s).";
        return RedirectToPage();
    }

    private async Task CargarCongeladosAsync(CancellationToken ct)
    {
        var snapshots = await _db.FrozenInventorySnapshots
            .Where(s => s.CompanyId == _currentCompany.CompanyId)
            .OrderByDescending(s => s.LoadedAt)
            .Take(200)
            .ToListAsync(ct);

        var branchIds = snapshots.Select(s => s.BranchId).Distinct().ToList();
        var branches = await _db.Branches
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var lineCounts = await _db.FrozenInventoryLines
            .Where(l => snapshots.Select(s => s.Id).Contains(l.SnapshotId))
            .GroupBy(l => l.SnapshotId)
            .Select(g => new { SnapshotId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SnapshotId, x => x.Count, ct);

        Congelados = snapshots.Select(s => new CongeladoRowDto(
            s.Id,
            branches.GetValueOrDefault(s.BranchId, "?"),
            s.InventoryNumber,
            s.FileName,
            lineCounts.GetValueOrDefault(s.Id, 0),
            s.LoadedAt
        )).ToList();
    }

    private async Task CargarSucursalesAsync(CancellationToken ct)
    {
        SucursalesActivas = await _db.Branches
            .Where(b => b.CompanyId == _currentCompany.CompanyId && b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);
    }

    public sealed class CargaCongeladoInput
    {
        public long? BranchId { get; set; }
        public string? InventoryNumber { get; set; }
        public IFormFile? Archivo { get; set; }
    }

    public sealed record CongeladoRowDto(
        long Id,
        string BranchName,
        string InventoryNumber,
        string FileName,
        int LineCount,
        DateTimeOffset LoadedAt);
}
