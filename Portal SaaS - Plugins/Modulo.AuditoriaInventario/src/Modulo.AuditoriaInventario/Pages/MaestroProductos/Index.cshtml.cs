using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

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
    private readonly ICurrentUserContext _currentUser;
    private readonly ISapSyncStatusTracker _syncStatus;
    private readonly IServiceScopeFactory _scopeFactory;

    public IndexModel(
        AuditoriaInventarioDbContext db,
        ICurrentCompanyAccessor currentCompany,
        ICurrentUserContext currentUser,
        ISapSyncStatusTracker syncStatus,
        IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _syncStatus = syncStatus;
        _scopeFactory = scopeFactory;
    }

    public int TotalProductos { get; private set; }
    public int TotalFiltrado { get; private set; }
    public IReadOnlyList<Models.Product> Productos { get; private set; } = Array.Empty<Models.Product>();
    public SapSyncEstado? SyncStatus { get; private set; }
    public IReadOnlyList<int> TamanosDisponibles => DocumentListViewModel.AvailablePageSizes;

    [BindProperty(SupportsGet = true)]
    public string? Buscar { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int TamanoPagina { get; set; } = 50;

    public int TotalPaginas => TotalFiltrado == 0 ? 1 : (int)Math.Ceiling(TotalFiltrado / (double)TamanoPagina);

    public async Task OnGetAsync(CancellationToken ct)
    {
        SyncStatus = _syncStatus.Obtener(_currentCompany.CompanyId);
        await CargarResumenAsync(ct);
    }

    /// <summary>
    /// Dispara la sincronización y vuelve al toque -- NO espera a que termine. Con
    /// ~200K artículos en SAP, la versión sincrónica anterior dejaba la pantalla
    /// "pegada" varios minutos (pedido explícito del dueño del proyecto,
    /// 2026-10-14). El trabajo real corre en un scope propio, desacoplado del
    /// ciclo de vida de este request -- mismo patrón que
    /// IntegrationSyncHostedService para poder resolver AuditoriaInventarioDbContext
    /// sin HttpContext (ver ICurrentCompanyOverride). Al terminar (ok o error) deja
    /// aviso en la campana de notificaciones (INotificationService) -- regla dura
    /// del proyecto, 2026-10-14: todo proceso de actualización/avance de un módulo
    /// tiene que poder avisar ahí, no solo en esta pantalla, para que se entere
    /// aunque ya haya navegado a otra parte.
    /// </summary>
    public IActionResult OnPostSincronizarSapAsync()
    {
        var companyId = _currentCompany.CompanyId;
        var userId = _currentUser.UserId;
        if (!_syncStatus.IntentarIniciar(companyId))
        {
            ErrorMessage = "Ya hay una sincronización con SAP en curso para esta compañía.";
            return RedirectToPage();
        }

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            // Tiene que fijarse ANTES de resolver cualquier cosa que dependa de
            // AuditoriaInventarioDbContext -- el override se lee recién cuando el
            // contenedor construye el DbContext, no antes.
            scope.ServiceProvider.GetRequiredService<ICurrentCompanyOverride>().Set(companyId);
            var sapSync = scope.ServiceProvider.GetRequiredService<ISapProductSyncService>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            const string url = "/auditoria-inventario/productos";

            try
            {
                var resultado = await sapSync.SincronizarAsync(
                    companyId,
                    onProgress: procesados => _syncStatus.ActualizarProgreso(companyId, procesados));

                var mensaje =
                    $"Sincronización con SAP: {resultado.Creados} creado(s), {resultado.Actualizados} actualizado(s) -- " +
                    $"{resultado.TotalSap} artículo(s) activos en SAP, {resultado.SinBarcode} sin código de barra cargable.";
                _syncStatus.Completar(companyId, mensaje);
                await notifications.TryEnqueueAsync(userId, "Maestro de Productos", mensaje, NotificationSeverity.Success, url);
            }
            catch (Exception ex)
            {
                var mensaje = GetErrorMessage(ex);
                _syncStatus.Fallar(companyId, mensaje);
                await notifications.TryEnqueueAsync(userId, "Maestro de Productos", mensaje, NotificationSeverity.Danger, url);
            }
        });

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
        var baseQuery = _db.Products.Where(p => p.CompanyId == _currentCompany.CompanyId);
        TotalProductos = await baseQuery.CountAsync(ct);

        var query = baseQuery;
        if (!string.IsNullOrWhiteSpace(Buscar))
        {
            var texto = Buscar.Trim();
            query = query.Where(p => EF.Functions.Like(p.Barcode, $"%{texto}%")
                || EF.Functions.Like(p.ProductCode, $"%{texto}%")
                || (p.Description != null && EF.Functions.Like(p.Description, $"%{texto}%")));
        }

        TotalFiltrado = await query.CountAsync(ct);

        var tamano = TamanosDisponibles.Contains(TamanoPagina) ? TamanoPagina : 50;
        var pagina = Math.Max(1, Pagina);

        Productos = await query
            .OrderByDescending(p => p.LoadedAt)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync(ct);
    }
}
