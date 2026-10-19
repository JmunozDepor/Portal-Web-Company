using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.AuditoriaInventario.Pages.Ajustes;

/// <summary>
/// Cola de ajustes propuestos, con el gate de aprobación manual obligatorio antes
/// de escribir en SapAdjustmentQueueItem -- ver AjusteService para el mapeo real.
/// Este módulo NUNCA llama a SAP directamente (decisión explícita del dueño del
/// proyecto, ver doc comment de SapAdjustmentQueueItem; confirmado 2026-10-15: por
/// ahora no hay carga masiva ni job armado del lado SAP, todo se tipea a mano en la
/// transacción estándar) -- el "Exportar a SAP" de acá es la alternativa real hoy:
/// un .xlsx con compañía/almacén/material/cantidad, listo para tipear en MIGO/MI07.
///
/// En SAP una diferencia de inventario se postea como INGRESO o SALIDA (nunca como
/// "cantidad con signo" suelta) -- acá se separa explícitamente, con el tipo de
/// movimiento 701/702 como SUGERENCIA (el estándar SAP para diferencias de
/// inventario físico vía MI07); si la configuración real usa otros códigos,
/// ajustarlo en AjusteRowDto.TipoMovimientoSugerido.
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAjusteService _ajusteService;
    private readonly INotificationService _notifications;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany, ICurrentUserContext currentUser,
        IAjusteService ajusteService, INotificationService notifications)
    {
        _db = db;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _ajusteService = ajusteService;
        _notifications = notifications;
    }

    public IReadOnlyList<AjusteRowDto> Ajustes { get; private set; } = Array.Empty<AjusteRowDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Ajustes = await ObtenerFilasAsync(ct);
    }

    public async Task<IActionResult> OnPostAprobarAsync(long id, CancellationToken ct)
    {
        var resultado = await _ajusteService.AprobarAsync(_db, id, _currentCompany.CompanyId, _currentUser.UserId, ct);
        if (!resultado.Exitoso)
        {
            ErrorMessage = resultado.Mensaje;
            return RedirectToPage();
        }

        SuccessMessage = resultado.Mensaje;

        // AprobarAsync ya validó compañía y estado -- acá solo hace falta saber a quién avisar.
        var ajuste = await _db.InventoryAdjustments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (ajuste is not null)
        {
            await NotificarProponenteAsync(ajuste, $"Tu ajuste #{ajuste.Id} fue aprobado y quedó en cola para SAP.", NotificationSeverity.Success, ct);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRechazarAsync(long id, CancellationToken ct)
    {
        // Mismo join por CompanyId que OnGetAsync -- InventoryAdjustment no lleva
        // company_id directo, así que el scoping por compañía tiene que pasar por
        // InventoryDifference -> InventorySession.
        var ajuste = await (
            from a in _db.InventoryAdjustments
            join d in _db.InventoryDifferences on a.DifferenceId equals d.Id
            join s in _db.InventorySessions on d.SessionId equals s.Id
            where a.Id == id && s.CompanyId == _currentCompany.CompanyId
            select a)
            .FirstOrDefaultAsync(ct);

        if (ajuste is null || ajuste.Status != "PROPOSED")
        {
            ErrorMessage = "El ajuste ya no está disponible para rechazar.";
            return RedirectToPage();
        }

        ajuste.Status = "REJECTED";
        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Ajuste rechazado.";
        await NotificarProponenteAsync(ajuste, $"Tu ajuste #{ajuste.Id} fue rechazado.", NotificationSeverity.Danger, ct);
        return RedirectToPage();
    }

    // GET /auditoria-inventario/ajustes?handler=Exportar
    // Solo los APROBADOS (los que ya tienen fila en SapAdjustmentQueueItem) -- un
    // PROPOSED todavía no pasó el gate de aprobación manual, no tiene sentido
    // mandarlo a SAP. Mismos campos que espera SapAdjustmentQueueItem, para que el
    // archivo sirva tal cual como entrada a lo que sea que lo cargue del lado SAP.
    public async Task<IActionResult> OnGetExportarAsync(CancellationToken ct)
    {
        var filas = (await ObtenerFilasAsync(ct)).Where(a => a.SapQueueStatus is not null).ToList();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Ajustes para SAP");

        var encabezados = new[]
        {
            "Id ajuste", "Movimiento", "Tipo mov. SAP sugerido", "Compañía SAP", "Almacén SAP", "Material SAP", "Cantidad",
            "Sucursal", "Nro. Inventario", "Código de barra", "Estado cola SAP",
            "Generado", "Procesado", "Nro. documento SAP", "Error",
        };
        for (int i = 0; i < encabezados.Length; i++)
            ws.Cell(1, i + 1).Value = encabezados[i];

        int fila = 2;
        foreach (var a in filas)
        {
            ws.Cell(fila, 1).Value = a.Id;
            ws.Cell(fila, 2).Value = a.EsIngreso ? "Ingreso" : "Salida";
            ws.Cell(fila, 3).Value = a.TipoMovimientoSugerido;
            ws.Cell(fila, 4).Value = a.SapCompanyCode;
            ws.Cell(fila, 5).Value = a.SapWarehouseCode;
            ws.Cell(fila, 6).Value = a.SapMaterialCode;
            ws.Cell(fila, 7).Value = a.CantidadAbsoluta;
            ws.Cell(fila, 8).Value = a.BranchName;
            ws.Cell(fila, 9).Value = a.InventoryNumber;
            ws.Cell(fila, 10).Value = a.Barcode;
            ws.Cell(fila, 11).Value = a.SapQueueStatus;
            ws.Cell(fila, 12).Value = a.SapGeneratedAt?.ToLocalTime().ToString("dd-MM-yyyy HH:mm");
            ws.Cell(fila, 13).Value = a.SapProcessedAt?.ToLocalTime().ToString("dd-MM-yyyy HH:mm") ?? "-";
            ws.Cell(fila, 14).Value = a.SapDocumentNumber;
            ws.Cell(fila, 15).Value = a.SapErrorMessage;
            fila++;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AjustesSAP_{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    /// <summary>Al que propuso el ajuste -- no a quien lo resolvió (si es la misma persona, ya lo ve en pantalla).</summary>
    private async Task NotificarProponenteAsync(InventoryAdjustment ajuste, string mensaje, string severity, CancellationToken ct)
    {
        if (ajuste.ProposedByUserId == _currentUser.UserId)
        {
            return;
        }

        await _notifications.TryEnqueueAsync(ajuste.ProposedByUserId, "Auditoría de inventario: ajuste", mensaje,
            severity, "/auditoria-inventario/ajustes", ct);
    }

    private async Task<List<AjusteRowDto>> ObtenerFilasAsync(CancellationToken ct)
    {
        var companyId = _currentCompany.CompanyId;

        // InventoryAdjustment no lleva company_id directo -- llega vía
        // InventoryDifference -> InventorySession, igual que el resto de los joins
        // de esta página.
        var ajustes = await (
            from a in _db.InventoryAdjustments
            join d in _db.InventoryDifferences on a.DifferenceId equals d.Id
            join s in _db.InventorySessions on d.SessionId equals s.Id
            where s.CompanyId == companyId
            orderby a.ProposedAt descending
            select new { Ajuste = a, Diferencia = d, Sesion = s })
            .Take(200)
            .ToListAsync(ct);

        var branchIds = ajustes.Select(x => x.Sesion.BranchId).Distinct().ToList();
        var branches = await _db.Branches
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var adjustmentIds = ajustes.Select(x => x.Ajuste.Id).ToList();
        var colaPorAjuste = await _db.SapAdjustmentQueueItems
            .Where(q => adjustmentIds.Contains(q.AdjustmentId))
            .ToDictionaryAsync(q => q.AdjustmentId, q => q, ct);

        return ajustes.Select(x =>
        {
            colaPorAjuste.TryGetValue(x.Ajuste.Id, out var cola);
            return new AjusteRowDto(
                x.Ajuste.Id,
                x.Ajuste.Status,
                x.Ajuste.ProposedAt,
                x.Ajuste.ApprovedAt,
                branches.GetValueOrDefault(x.Sesion.BranchId, "?"),
                x.Sesion.InventoryNumber,
                x.Diferencia.Barcode,
                x.Diferencia.QuantityDiff,
                cola?.SapCompanyCode,
                cola?.SapWarehouseCode,
                cola?.SapMaterialCode,
                cola?.Quantity,
                cola?.Status,
                cola?.GeneratedAt,
                cola?.ProcessedAt,
                cola?.SapDocumentNumber,
                cola?.ErrorMessage);
        }).ToList();
    }

    public sealed record AjusteRowDto(
        long Id,
        string Status,
        DateTimeOffset ProposedAt,
        DateTimeOffset? ApprovedAt,
        string BranchName,
        string InventoryNumber,
        string Barcode,
        int QuantityDiff,
        string? SapCompanyCode,
        string? SapWarehouseCode,
        string? SapMaterialCode,
        decimal? SapQuantity,
        string? SapQueueStatus,
        DateTimeOffset? SapGeneratedAt,
        DateTimeOffset? SapProcessedAt,
        string? SapDocumentNumber,
        string? SapErrorMessage)
    {
        /// <summary>
        /// En SAP una diferencia de inventario se postea como ingreso o salida
        /// (tipos de movimiento 701/702 en el escenario estándar de diferencias de
        /// inventario físico, MI07/MIGO) -- nunca como "cantidad con signo" suelta,
        /// que es lo que esta cola guardaba hasta ahora. Calculado desde
        /// QuantityDiff (siempre disponible, incluso antes de aprobar) para que ya
        /// se vea en pantalla qué va a salir antes de aprobarlo.
        /// </summary>
        public bool EsIngreso => QuantityDiff > 0;
        public int CantidadAbsoluta => Math.Abs(QuantityDiff);
        public string TipoMovimientoSugerido => QuantityDiff switch
        {
            > 0 => "701 -- Entrada (sobrante)",
            < 0 => "702 -- Salida (faltante)",
            _ => "Sin diferencia",
        };
    }
}
