using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.MaestroProductos;

/// <summary>
/// Maestro de productos (EAN) del módulo -- hoy solo carga vía sincronización con
/// SAP (ISapProductSyncService); la carga manual por Excel queda para una
/// iteración futura (ver PENDIENTE.md).
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ISapProductSyncService _sapSync;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany, ISapProductSyncService sapSync)
    {
        _db = db;
        _currentCompany = currentCompany;
        _sapSync = sapSync;
    }

    public int TotalProductos { get; private set; }
    public IReadOnlyList<Models.Product> UltimosCargados { get; private set; } = Array.Empty<Models.Product>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        await CargarResumenAsync(ct);
    }

    public async Task<IActionResult> OnPostSincronizarSapAsync(CancellationToken ct)
    {
        try
        {
            var resultado = await _sapSync.SincronizarAsync(_currentCompany.CompanyId, ct);
            SuccessMessage =
                $"Sincronización con SAP: {resultado.Creados} creado(s), {resultado.Actualizados} actualizado(s) -- " +
                $"{resultado.TotalSap} artículo(s) activos en SAP, {resultado.SinBarcode} sin código de barra cargable.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    /// <summary>
    /// Genera el mismo maestro que la PWA descarga vía API (barcode/productCode +
    /// sucursales), pero como archivo para copiar manualmente a un equipo sin
    /// conectividad -- ver MantenedorPage.tsx > "Cargar desde archivo (JSON)".
    /// </summary>
    public async Task<IActionResult> OnGetDescargarJsonAsync(CancellationToken ct)
    {
        var companyId = _currentCompany.CompanyId;

        var productos = await _db.Products
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.Id)
            .Select(p => new { id = p.Id, barcode = p.Barcode, productCode = p.ProductCode })
            .ToListAsync(ct);

        var sucursales = await _db.Branches
            .Where(b => b.CompanyId == companyId && b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new { id = b.Id, branchCode = b.BranchCode, name = b.Name })
            .ToListAsync(ct);

        var payload = new { productos, sucursales };
        var json = JsonSerializer.Serialize(payload);
        var fileName = $"maestro-auditoria-inventario-{DateTime.UtcNow:yyyyMMdd-HHmm}.json";
        return File(Encoding.UTF8.GetBytes(json), "application/json", fileName);
    }

    private async Task CargarResumenAsync(CancellationToken ct)
    {
        var query = _db.Products.Where(p => p.CompanyId == _currentCompany.CompanyId);
        TotalProductos = await query.CountAsync(ct);
        UltimosCargados = await query
            .OrderByDescending(p => p.LoadedAt)
            .Take(50)
            .ToListAsync(ct);
    }
}
