using Microsoft.AspNetCore.Mvc;
using Modulo.FacturasProveedor.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.FacturasProveedor.Pages.FacturasProveedor.MisAprobaciones;

/// <summary>Facturas por decidir de los grupos del usuario conectado (Rendiciones › Aprobaciones como referencia).</summary>
public sealed class IndexModel : FacturasProveedorPageModelBase
{
    private readonly IReviewQueryService _queries;

    public IndexModel(IReviewQueryService queries, ICurrentUserContext currentUser) : base(currentUser) => _queries = queries;

    protected override string MenuCode => "mis-aprobaciones";

    public IReadOnlyList<ReviewRow> Rows { get; private set; } = Array.Empty<ReviewRow>();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct = default)
    {
        if (!await HasActionAsync(PortalActions.View)) return Forbid();
        Rows = await _queries.ListMineAsync(ct);
        return Page();
    }
}
