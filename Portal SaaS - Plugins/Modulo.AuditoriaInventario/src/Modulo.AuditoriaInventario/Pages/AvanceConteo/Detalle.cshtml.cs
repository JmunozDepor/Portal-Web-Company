using Microsoft.AspNetCore.Mvc;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.AvanceConteo;

/// <summary>
/// Grilla en pantalla (no Excel) del detalle por producto de UNA tarea de conteo --
/// se abre al hacer clic en una fila de Index.cshtml. Misma fuente de datos que el
/// export a Excel (Pages/AvanceConteo/Index.OnGetExportarDetalleAsync), ver
/// Servicios/DetalleConteoBuilder -- para no mantener la cuenta dos veces.
///
/// También es donde se dispara "Proponer ajustes" (IAjusteService.ProponerTodosAsync)
/// -- el paso que faltaba entre que DiferenciaEngine calcula InventoryDifference al
/// cerrar y que algo aparezca en Pages/Ajustes para aprobar.
/// </summary>
public sealed class DetalleModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAjusteService _ajusteService;

    public DetalleModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany, ICurrentUserContext currentUser, IAjusteService ajusteService)
    {
        _db = db;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _ajusteService = ajusteService;
    }

    public DetalleConteoBuilder.Resultado? Detalle { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid sessionId, CancellationToken ct)
    {
        Detalle = await DetalleConteoBuilder.ConstruirAsync(_db, _currentCompany.CompanyId, sessionId, ct);
        if (Detalle is null) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostProponerAjustesAsync(Guid sessionId, CancellationToken ct)
    {
        var creados = await _ajusteService.ProponerTodosAsync(_db, sessionId, _currentCompany.CompanyId, _currentUser.UserId, ct);
        SuccessMessage = creados > 0
            ? $"{creados} ajuste(s) propuesto(s) -- revisalos en \"Ajustes\" para aprobar o rechazar."
            : "No había diferencias nuevas para proponer (ya estaban todas propuestas, o no hay congelado cargado para comparar).";
        return RedirectToPage(new { sessionId });
    }
}
