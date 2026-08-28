using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Compras;
using PortalSaas.Core.Inventario;
using PortalSaas.Core.Ventas;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.DocumentPermissions;

/// <summary>
/// Override por organización de "permite crear documento" para los 3 motores
/// genéricos (ver OrganizationDocumentPermission/IOrganizationDocumentPermissionService,
/// CLAUDE.md). Antes de esta pantalla la tabla existía sin ninguna forma de cargarse
/// salvo SQL directo -- Engine/DocumentType construidos acá desde los catálogos
/// estáticos reales de cada motor (SalesDocumentTypeCatalog/PurchaseDocumentTypeCatalog/
/// InventoryDocumentTypeCatalog), nunca tipeados a mano, para no poder desincronizarse
/// de los tipos reales si se agrega un octavo tipo de Venta o similar.
/// </summary>
[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private const string OptionDefault = "default";
    private const string OptionAllow = "allow";
    private const string OptionBlock = "block";

    private readonly PortalSaasDbContext _db;

    public IndexModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public Organization Organization { get; private set; } = null!;
    public List<RowViewModel> Rows { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        await LoadRowsAsync(organizationId);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid organizationId, Dictionary<string, string> selection)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        var existentes = await _db.OrganizationDocumentPermissions
            .Where(p => p.OrganizationId == organizationId)
            .ToListAsync();

        foreach (var (engine, documentType, _) in BuildCatalogRows())
        {
            var key = $"{engine}.{documentType}";
            var elegido = selection.GetValueOrDefault(key, OptionDefault);
            var existente = existentes.FirstOrDefault(p => p.Engine == engine && p.DocumentType == documentType);

            if (elegido == OptionDefault)
            {
                if (existente is not null)
                {
                    _db.OrganizationDocumentPermissions.Remove(existente);
                }

                continue;
            }

            var canCreate = elegido == OptionAllow;

            if (existente is not null)
            {
                existente.CanCreate = canCreate;
                existente.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _db.OrganizationDocumentPermissions.Add(new OrganizationDocumentPermission
                {
                    OrganizationId = organizationId,
                    Engine = engine,
                    DocumentType = documentType,
                    CanCreate = canCreate,
                });
            }
        }

        await _db.SaveChangesAsync();

        Organization = organization;
        await LoadRowsAsync(organizationId);

        TempData["Mensaje"] = "Permisos de creación de documentos actualizados.";
        return RedirectToPage(new { organizationId });
    }

    private async Task LoadRowsAsync(Guid organizationId)
    {
        var overrides = await _db.OrganizationDocumentPermissions
            .Where(p => p.OrganizationId == organizationId)
            .ToListAsync();

        Rows = BuildCatalogRows()
            .Select(row =>
            {
                var overrideRow = overrides.FirstOrDefault(o => o.Engine == row.Engine && o.DocumentType == row.DocumentType);
                var selected = overrideRow is null ? OptionDefault : overrideRow.CanCreate ? OptionAllow : OptionBlock;
                return new RowViewModel(row.Engine, row.DocumentType, row.DefaultCanCreate, selected);
            })
            .OrderBy(r => r.Engine).ThenBy(r => r.DocumentType)
            .ToList();
    }

    /// <summary>Engine/DocumentType/DefaultCanCreate reales de los 3 catálogos estáticos -- nunca tipeados a mano.</summary>
    private static IEnumerable<(string Engine, string DocumentType, bool DefaultCanCreate)> BuildCatalogRows()
    {
        foreach (var (type, entry) in SalesDocumentTypeCatalog.Entries)
        {
            yield return ("Sales", type.ToString(), entry.DefaultCanCreate);
        }

        foreach (var (type, entry) in PurchaseDocumentTypeCatalog.Entries)
        {
            yield return ("Purchase", type.ToString(), entry.DefaultCanCreate);
        }

        foreach (var (type, entry) in InventoryDocumentTypeCatalog.Entries)
        {
            yield return ("Inventory", type.ToString(), entry.DefaultCanCreate);
        }
    }

    public sealed record RowViewModel(string Engine, string DocumentType, bool DefaultCanCreate, string Selected);
}
