using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.AuditoriaInventario.Pages.AvanceConteo;

/// <summary>
/// Pantalla única de conteo (2026-10-15, reemplaza Sesiones/Congelados/Diferencias
/// -- consolidación pedida por el dueño del proyecto, ver
/// docs/superpowers/plans si corresponde). Acá se:
///  - CREAN las tareas de conteo (antes las creaba la PWA al primer sync; ahora la
///    PWA solo se suma a una tarea ya creada acá, ver GetSesionesAbiertasAsync).
///  - CARGA el congelado (manual, por tarea; SAP directo queda para una segunda
///    etapa, decisión explícita del dueño del proyecto).
///  - CIERRA una tarea a la fuerza desde el portal (además del cierre normal
///    desde la PWA) -- una vez CLOSED, AuditoriaInventarioApiService rechaza
///    cualquier sync posterior contra esa sesión.
///  - VE congelado/contado/diferencia en vivo, en unidad y en monto (mismo
///    criterio de valorización que IDiferenciaEngine: UnitCost del congelado,
///    0 si un código contado no tiene costo cargado).
///  - EXPORTA a Excel (resumen de la grilla, y detalle por sector/producto de
///    una tarea puntual, con capturador y monto).
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDiferenciaEngine _diferenciaEngine;
    private readonly INotificationService _notifications;

    private const string RutaPagina = "/auditoria-inventario/avance-conteo";

    public IndexModel(
        AuditoriaInventarioDbContext db,
        ICurrentCompanyAccessor currentCompany,
        ICurrentUserContext currentUser,
        IDiferenciaEngine diferenciaEngine,
        INotificationService notifications)
    {
        _db = db;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _diferenciaEngine = diferenciaEngine;
        _notifications = notifications;
    }

    public IReadOnlyList<AvanceRowDto> Filas { get; private set; } = Array.Empty<AvanceRowDto>();
    public IReadOnlyList<Branch> SucursalesActivas { get; private set; } = Array.Empty<Branch>();
    public IReadOnlyList<CaptureUser> CapturadoresActivos { get; private set; } = Array.Empty<CaptureUser>();

    /// <summary>Busca por coincidencia parcial contra el Nro. de Inventario (la "tarea").</summary>
    [BindProperty(SupportsGet = true)]
    public string? TareaFiltro { get; set; }

    [BindProperty(SupportsGet = true)]
    public long? BranchIdFiltro { get; set; }

    /// <summary>Filtra sesiones creadas desde esta fecha en adelante (inclusive) -- compara contra StartedAt.</summary>
    [BindProperty(SupportsGet = true)]
    public DateOnly? FechaDesde { get; set; }

    [BindProperty]
    public CrearTareaInput NuevaTarea { get; set; } = new();

    [BindProperty]
    public CargarCongeladoInput Congelado { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        await CargarTodoAsync(ct);
    }

    // POST /auditoria-inventario/avance-conteo?handler=CrearTarea
    public async Task<IActionResult> OnPostCrearTareaAsync(CancellationToken ct)
    {
        if (NuevaTarea.BranchId is null || string.IsNullOrWhiteSpace(NuevaTarea.InventoryNumber) || NuevaTarea.ResponsibleUserId is null)
        {
            ErrorMessage = "Completá sucursal, nro. de inventario y capturador asignado.";
            await CargarTodoAsync(ct);
            return Page();
        }

        var companyId = _currentCompany.CompanyId;

        if (!await _db.Branches.AnyAsync(b => b.Id == NuevaTarea.BranchId.Value && b.CompanyId == companyId, ct))
        {
            ErrorMessage = "La sucursal seleccionada no es válida.";
            await CargarTodoAsync(ct);
            return Page();
        }

        if (!await _db.CaptureUsers.AnyAsync(u => u.Id == NuevaTarea.ResponsibleUserId.Value && u.CompanyId == companyId && u.IsActive, ct))
        {
            ErrorMessage = "El capturador asignado no es válido.";
            await CargarTodoAsync(ct);
            return Page();
        }

        var sesion = new InventorySession
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            BranchId = NuevaTarea.BranchId.Value,
            InventoryNumber = NuevaTarea.InventoryNumber.Trim(),
            ResponsibleUserId = NuevaTarea.ResponsibleUserId.Value,
            StartedAt = DateTimeOffset.UtcNow,
            Status = "ACTIVE",
            ValidateAgainstMaster = NuevaTarea.ValidateAgainstMaster,
        };
        _db.InventorySessions.Add(sesion);
        await _db.SaveChangesAsync(ct);

        SuccessMessage = $"Tarea de conteo creada para el Nro. de Inventario {sesion.InventoryNumber}.";
        await _notifications.TryEnqueueAsync(_currentUser.UserId, "Auditoría de inventario: nueva tarea",
            $"Se creó la tarea de conteo {sesion.InventoryNumber} -- ya está disponible para el equipo de captura.",
            NotificationSeverity.Info, RutaPagina, ct);

        return RedirectToPage();
    }

    // GET /auditoria-inventario/avance-conteo?handler=DescargarPlantillaCongelado
    // CongeladoExcelParser SIEMPRE saltea la fila 1 (la trata como encabezado,
    // mire o no texto real -- ver el bucle "for (var i = 1; ...)" del parser), así
    // que la plantilla trae ese encabezado real y los datos arrancan en la fila 2.
    // Código de barra (A) y código de producto (D) son alternativos: alcanza con
    // uno de los dos por fila.
    public IActionResult OnGetDescargarPlantillaCongelado()
    {
        using var workbook = new XLWorkbook();

        var wsDatos = workbook.Worksheets.Add("Congelado");
        wsDatos.Cell(1, 1).Value = "Codigo de barra";
        wsDatos.Cell(1, 2).Value = "Cantidad";
        wsDatos.Cell(1, 3).Value = "Costo unitario";
        wsDatos.Cell(1, 4).Value = "Codigo de producto";
        wsDatos.Row(1).Style.Font.Bold = true;
        wsDatos.Column(1).Width = 20;
        wsDatos.Column(2).Width = 12;
        wsDatos.Column(3).Width = 14;
        wsDatos.Column(4).Width = 20;
        // Sin fila de ejemplo acá a propósito -- cualquier fila en esta hoja (salvo
        // la 1) se lee como un código real, cargarla sin borrar el ejemplo primero
        // ensuciaría el congelado con una línea que no existe.

        var wsInstrucciones = workbook.Worksheets.Add("Instrucciones");
        wsInstrucciones.Column(1).Width = 70;
        var filas = new[]
        {
            "Cómo completar la hoja \"Congelado\":",
            "",
            "La fila 1 es el encabezado (ya viene escrito) -- los datos arrancan en la fila 2.",
            "",
            "- Columna A: código de barra.",
            "- Columna B: cantidad congelada (número entero).",
            "- Columna C: costo unitario (opcional -- si se deja vacío, esa línea queda sin valorización en los reportes de monto, $0).",
            "- Columna D: código de producto.",
            "",
            "Código de barra (A) y código de producto (D) son ALTERNATIVOS: completá al menos uno de los dos por fila, no hace falta llenar ambos. Si solo cargás el código de producto, tiene que existir en el Maestro de Productos para poder resolver el código de barra.",
            "",
            "Ejemplo de una fila válida (columnas A / B / C / D):",
        };
        for (int i = 0; i < filas.Length; i++)
            wsInstrucciones.Cell(i + 1, 1).Value = filas[i];
        wsInstrucciones.Cell(1, 1).Style.Font.Bold = true;

        var filaEjemplo = filas.Length + 1;
        wsInstrucciones.Cell(filaEjemplo, 1).Value = "7801234567890";
        wsInstrucciones.Cell(filaEjemplo, 2).Value = 12;
        wsInstrucciones.Cell(filaEjemplo, 3).Value = 1590.50;
        wsInstrucciones.Cell(filaEjemplo, 4).Value = "372860C-102-2X";
        wsInstrucciones.Range(filaEjemplo, 1, filaEjemplo, 4).Style.Font.Italic = true;

        workbook.Worksheets.Worksheet("Congelado").SetTabActive();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Plantilla_Congelado.xlsx");
    }

    // POST /auditoria-inventario/avance-conteo?handler=CargarCongelado
    public async Task<IActionResult> OnPostCargarCongeladoAsync(CancellationToken ct)
    {
        await CargarTodoAsync(ct);

        if (Congelado.SessionId is null || Congelado.Archivo is null || Congelado.Archivo.Length == 0)
        {
            ErrorMessage = "Seleccioná la tarea y un archivo Excel para cargar.";
            return Page();
        }

        var companyId = _currentCompany.CompanyId;
        var sesion = await _db.InventorySessions
            .FirstOrDefaultAsync(s => s.Id == Congelado.SessionId.Value && s.CompanyId == companyId, ct);
        if (sesion is null)
        {
            ErrorMessage = "La tarea de conteo seleccionada no existe.";
            return Page();
        }

        CongeladoParseResult resultado;
        await using (var stream = Congelado.Archivo.OpenReadStream())
        {
            resultado = CongeladoExcelParser.Parse(stream);
        }

        if (resultado.Errores.Count > 0)
        {
            ErrorMessage = $"El archivo tiene {resultado.Errores.Count} fila(s) con errores -- corregilas y volvé a subirlo. " +
                string.Join(" | ", resultado.Errores.Take(5).Select(e => $"Fila {e.NumeroFila}: {e.Mensaje}"));
            return Page();
        }

        if (resultado.Filas.Count == 0)
        {
            ErrorMessage = "El archivo no tiene filas para cargar.";
            return Page();
        }

        // FrozenInventoryLine.Barcode es la clave real que usa todo el resto del
        // módulo (DiferenciaEngine, AvanceConteo) -- una fila que vino solo con
        // código de producto necesita resolverlo contra el maestro ACÁ, el parser
        // no tiene acceso a la base (ver CongeladoExcelParser). Todo o nada, mismo
        // criterio que los errores de parseo de arriba: si algún código de producto
        // no existe, se rechaza el archivo completo en vez de cargarlo a medias.
        var codigosProductoAResolver = resultado.Filas
            .Where(f => f.Barcode is null)
            .Select(f => f.ProductCode!)
            .Distinct()
            .ToList();
        var barcodePorProductCode = codigosProductoAResolver.Count == 0
            ? new Dictionary<string, string>()
            : await _db.Products
                .Where(p => p.CompanyId == companyId && codigosProductoAResolver.Contains(p.ProductCode))
                .ToDictionaryAsync(p => p.ProductCode, p => p.Barcode, ct);

        var codigosNoEncontrados = codigosProductoAResolver.Where(pc => !barcodePorProductCode.ContainsKey(pc)).ToList();
        if (codigosNoEncontrados.Count > 0)
        {
            ErrorMessage = $"{codigosNoEncontrados.Count} código(s) de producto no existen en el Maestro de Productos -- " +
                $"no se pudo resolver el código de barra: {string.Join(", ", codigosNoEncontrados.Take(5))}" +
                (codigosNoEncontrados.Count > 5 ? "…" : "") + ". Corregilos (o completá el código de barra) y volvé a subir el archivo.";
            return Page();
        }

        var snapshot = new FrozenInventorySnapshot
        {
            CompanyId = companyId,
            BranchId = sesion.BranchId,
            InventoryNumber = sesion.InventoryNumber,
            LoadedByUserId = _currentUser.UserId,
            FileName = Congelado.Archivo.FileName,
        };
        _db.FrozenInventorySnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);

        _db.FrozenInventoryLines.AddRange(resultado.Filas.Select(f => new FrozenInventoryLine
        {
            SnapshotId = snapshot.Id,
            Barcode = f.Barcode ?? barcodePorProductCode[f.ProductCode!],
            ProductCode = f.ProductCode,
            Quantity = f.Quantity,
            UnitCost = f.UnitCost,
        }));
        await _db.SaveChangesAsync(ct);

        SuccessMessage = $"Congelado cargado para {sesion.InventoryNumber}: {resultado.Filas.Count} línea(s).";
        await _notifications.TryEnqueueAsync(_currentUser.UserId, "Auditoría de inventario: congelado",
            $"Congelado del inventario {sesion.InventoryNumber} cargado: {resultado.Filas.Count} línea(s).",
            NotificationSeverity.Success, RutaPagina, ct);

        return RedirectToPage();
    }

    // POST /auditoria-inventario/avance-conteo?handler=Cerrar&sessionId=...
    // Cierre forzado desde el portal -- además del cierre normal que hace la PWA.
    // Una vez CLOSED, AuditoriaInventarioApiService.UpsertSesionAsync/UploadCapturasAsync
    // rechazan cualquier sync posterior contra esta sesión.
    public async Task<IActionResult> OnPostCerrarAsync(Guid sessionId, CancellationToken ct)
    {
        var companyId = _currentCompany.CompanyId;
        var sesion = await _db.InventorySessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.CompanyId == companyId, ct);

        if (sesion is null)
        {
            ErrorMessage = "La tarea de conteo no existe.";
        }
        else if (sesion.Status == "CLOSED")
        {
            ErrorMessage = "Esa tarea ya está cerrada.";
        }
        else
        {
            sesion.Status = "CLOSED";
            sesion.ClosedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            await _diferenciaEngine.CalcularDiferenciasAsync(_db, sesion.Id, ct);

            SuccessMessage = $"Tarea {sesion.InventoryNumber} cerrada. No admite más sincronizaciones.";
            await _notifications.TryEnqueueAsync(_currentUser.UserId, "Auditoría de inventario: tarea cerrada",
                $"La tarea de conteo {sesion.InventoryNumber} se cerró desde el portal.",
                NotificationSeverity.Info, RutaPagina, ct);
        }

        return RedirectToPage();
    }

    // GET /auditoria-inventario/avance-conteo?handler=ExportarResumen
    public async Task<IActionResult> OnGetExportarResumenAsync(CancellationToken ct)
    {
        var filas = await ObtenerFilasAsync(ct);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Avance");

        var encabezados = new[]
        {
            "Sucursal", "Nro. Inventario", "Responsable", "Estado", "Creada", "Última sincronización",
            "Cant. congelada", "Monto congelado", "Cant. contada", "Monto contado", "Diferencia cant.", "Diferencia monto",
        };
        for (int i = 0; i < encabezados.Length; i++)
            ws.Cell(1, i + 1).Value = encabezados[i];

        int fila = 2;
        foreach (var f in filas)
        {
            ws.Cell(fila, 1).Value = f.BranchName;
            ws.Cell(fila, 2).Value = f.InventoryNumber;
            ws.Cell(fila, 3).Value = f.ResponsibleUserName;
            ws.Cell(fila, 4).Value = f.Status;
            ws.Cell(fila, 5).Value = f.StartedAt.ToLocalTime().ToString("dd-MM-yyyy HH:mm");
            ws.Cell(fila, 6).Value = f.UltimaSincronizacion?.ToLocalTime().ToString("dd-MM-yyyy HH:mm") ?? "Sin sincronizar";
            if (f.Congelado.HasValue) ws.Cell(fila, 7).Value = f.Congelado.Value; else ws.Cell(fila, 7).Value = "Sin congelado";
            if (f.CongeladoMonto.HasValue) ws.Cell(fila, 8).Value = f.CongeladoMonto.Value; else ws.Cell(fila, 8).Value = "—";
            ws.Cell(fila, 9).Value = f.Contado;
            ws.Cell(fila, 10).Value = f.ContadoMonto;
            if (f.Diferencia.HasValue) ws.Cell(fila, 11).Value = f.Diferencia.Value; else ws.Cell(fila, 11).Value = "—";
            if (f.DiferenciaMonto.HasValue) ws.Cell(fila, 12).Value = f.DiferenciaMonto.Value; else ws.Cell(fila, 12).Value = "—";
            fila++;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AvanceConteo_{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    // GET /auditoria-inventario/avance-conteo?handler=ExportarDetalle&sessionId=...
    // Detalle de UNA tarea puntual: "Por sector" (trazabilidad de dónde y quién contó
    // cada código, zona=sector, equipo=capturador logueado) y "Por producto"
    // (congelado vs. contado vs. diferencia, en unidad y monto -- mismo criterio de
    // valorización que IDiferenciaEngine). Calculado EN VIVO, funciona también para
    // tareas todavía ACTIVAS.
    public async Task<IActionResult> OnGetExportarDetalleAsync(Guid sessionId, CancellationToken ct)
    {
        var companyId = _currentCompany.CompanyId;

        var detalle = await DetalleConteoBuilder.ConstruirAsync(_db, companyId, sessionId, ct);
        if (detalle is null) return NotFound();
        var sesion = detalle.Sesion;
        var branchName = detalle.BranchName;

        var capturas = await _db.InventoryCaptures
            .Where(c => c.SessionId == sessionId)
            .ToListAsync(ct);

        var sectorIds = capturas.Select(c => c.SectorId).Distinct().ToList();
        var sectores = await _db.InventorySectors
            .Where(s => sectorIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var capturadorIds = capturas.Select(c => c.CapturedByUserId).Distinct().ToList();
        var capturadores = await _db.CaptureUsers
            .Where(u => capturadorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Username, ct);

        var barcodesCapturados = capturas.Select(c => c.Barcode).Distinct().ToList();
        var productos = await _db.Products
            .Where(p => p.CompanyId == companyId && barcodesCapturados.Contains(p.Barcode))
            .ToDictionaryAsync(p => p.Barcode, p => p, ct);

        using var workbook = new XLWorkbook();

        // Hoja 1: cada captura agrupada por sector+código+capturador -- zona y equipo
        // (el capturador logueado hace de "equipo", ver decisión del dueño del
        // proyecto 2026-10-15: no hay dispositivo físico separado del usuario).
        var wsSector = workbook.Worksheets.Add("Por sector y equipo");
        var encabezadosSector = new[] { "Sector", "Capturador", "Código barra", "Código producto", "Descripción", "Marca", "Cantidad", "¿En maestro?", "Última captura" };
        for (int i = 0; i < encabezadosSector.Length; i++)
            wsSector.Cell(1, i + 1).Value = encabezadosSector[i];

        var porSectorYCapturador = capturas
            .GroupBy(c => (c.SectorId, c.CapturedByUserId, c.Barcode))
            .Select(g => new
            {
                g.Key.SectorId,
                g.Key.CapturedByUserId,
                g.Key.Barcode,
                Cantidad = g.Sum(x => x.Quantity),
                ProductCode = g.Select(x => x.ProductCode).FirstOrDefault(pc => pc != null),
                EnMaestro = g.Select(x => x.InMaster).FirstOrDefault(im => im != null),
                UltimaCaptura = g.Max(x => x.CapturedAt),
            })
            .OrderBy(x => sectores.GetValueOrDefault(x.SectorId, "?"))
            .ThenBy(x => capturadores.GetValueOrDefault(x.CapturedByUserId, "?"))
            .ThenBy(x => x.Barcode)
            .ToList();

        int filaSector = 2;
        foreach (var r in porSectorYCapturador)
        {
            productos.TryGetValue(r.Barcode, out var producto);
            wsSector.Cell(filaSector, 1).Value = sectores.GetValueOrDefault(r.SectorId, "?");
            wsSector.Cell(filaSector, 2).Value = capturadores.GetValueOrDefault(r.CapturedByUserId, "?");
            wsSector.Cell(filaSector, 3).Value = r.Barcode;
            wsSector.Cell(filaSector, 4).Value = r.ProductCode ?? producto?.ProductCode;
            wsSector.Cell(filaSector, 5).Value = producto?.Description;
            wsSector.Cell(filaSector, 6).Value = producto?.Brand;
            wsSector.Cell(filaSector, 7).Value = r.Cantidad;
            wsSector.Cell(filaSector, 8).Value = r.EnMaestro.HasValue ? (r.EnMaestro.Value ? "Sí" : "No") : "No evaluado";
            wsSector.Cell(filaSector, 9).Value = r.UltimaCaptura.ToLocalTime().ToString("dd-MM-yyyy HH:mm");
            filaSector++;
        }
        wsSector.Columns().AdjustToContents();

        // Hoja 2: congelado vs. contado por producto (toda la tarea, sin desglose por
        // sector/capturador) -- la comparación que de verdad importa, en unidad y
        // monto. Misma fuente que la grilla en pantalla (Detalle.cshtml), ver
        // DetalleConteoBuilder.
        var wsProducto = workbook.Worksheets.Add("Por producto");
        var encabezadosProducto = new[] { "Código barra", "Código producto", "Descripción", "Marca", "Congelado", "Contado", "Diferencia cant.", "Costo unitario", "Monto congelado", "Monto contado", "Diferencia monto" };
        for (int i = 0; i < encabezadosProducto.Length; i++)
            wsProducto.Cell(1, i + 1).Value = encabezadosProducto[i];

        int filaProducto = 2;
        foreach (var f in detalle.Filas)
        {
            wsProducto.Cell(filaProducto, 1).Value = f.Barcode;
            wsProducto.Cell(filaProducto, 2).Value = f.ProductCode;
            wsProducto.Cell(filaProducto, 3).Value = f.Description;
            wsProducto.Cell(filaProducto, 4).Value = f.Brand;
            if (f.Congelado.HasValue) wsProducto.Cell(filaProducto, 5).Value = f.Congelado.Value; else wsProducto.Cell(filaProducto, 5).Value = "Sin congelado";
            wsProducto.Cell(filaProducto, 6).Value = f.Contado;
            if (f.DiferenciaCantidad.HasValue) wsProducto.Cell(filaProducto, 7).Value = f.DiferenciaCantidad.Value; else wsProducto.Cell(filaProducto, 7).Value = "—";
            wsProducto.Cell(filaProducto, 8).Value = f.UnitCost;
            if (f.MontoCongelado.HasValue) wsProducto.Cell(filaProducto, 9).Value = f.MontoCongelado.Value; else wsProducto.Cell(filaProducto, 9).Value = "—";
            wsProducto.Cell(filaProducto, 10).Value = f.MontoContado;
            if (f.MontoDiferencia.HasValue) wsProducto.Cell(filaProducto, 11).Value = f.MontoDiferencia.Value; else wsProducto.Cell(filaProducto, 11).Value = "—";
            filaProducto++;
        }

        // Fila de totales -- misma unidad/monto que se ve al pie de la grilla en pantalla.
        wsProducto.Cell(filaProducto, 1).Value = "TOTAL";
        wsProducto.Cell(filaProducto, 5).Value = detalle.TotalCongelado;
        wsProducto.Cell(filaProducto, 6).Value = detalle.TotalContado;
        wsProducto.Cell(filaProducto, 7).Value = detalle.TotalDiferencia;
        wsProducto.Cell(filaProducto, 9).Value = detalle.TotalMontoCongelado;
        wsProducto.Cell(filaProducto, 10).Value = detalle.TotalMontoContado;
        wsProducto.Cell(filaProducto, 11).Value = detalle.TotalMontoDiferencia;
        wsProducto.Row(filaProducto).Style.Font.Bold = true;

        wsProducto.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var nombreArchivo = $"Conteo_{branchName}_{sesion.InventoryNumber}".Replace(" ", "_");
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{nombreArchivo}.xlsx");
    }

    private async Task CargarTodoAsync(CancellationToken ct)
    {
        Filas = await ObtenerFilasAsync(ct);
        SucursalesActivas = await _db.Branches
            .Where(b => b.CompanyId == _currentCompany.CompanyId && b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);
        CapturadoresActivos = await _db.CaptureUsers
            .Where(u => u.CompanyId == _currentCompany.CompanyId && u.IsActive)
            .OrderBy(u => u.Username)
            .ToListAsync(ct);
    }

    private async Task<List<AvanceRowDto>> ObtenerFilasAsync(CancellationToken ct)
    {
        var companyId = _currentCompany.CompanyId;

        var query = _db.InventorySessions.Where(s => s.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(TareaFiltro))
        {
            query = query.Where(s => s.InventoryNumber.Contains(TareaFiltro));
        }
        if (BranchIdFiltro.HasValue)
        {
            query = query.Where(s => s.BranchId == BranchIdFiltro.Value);
        }
        if (FechaDesde.HasValue)
        {
            // DateOnly -> inicio del día en UTC. StartedAt se guarda en UTC (DateTimeOffset.UtcNow
            // por default en InventorySession), comparar en UTC evita el corrimiento de huso
            // horario que daría un falso "no hay nada desde hoy" cerca de medianoche.
            var desde = new DateTimeOffset(FechaDesde.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(s => s.StartedAt >= desde);
        }

        // Filtrado ANTES del Take(200) a propósito -- si no, un filtro angosto podía
        // quedar vacío porque las 200 sesiones más recientes (sin filtrar) no incluían
        // ninguna que lo cumpliera.
        var sesiones = await query
            .OrderByDescending(s => s.StartedAt)
            .Take(200)
            .ToListAsync(ct);

        var branchIds = sesiones.Select(s => s.BranchId).Distinct().ToList();
        var branches = await _db.Branches
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var userIds = sesiones.Select(s => s.ResponsibleUserId).Distinct().ToList();
        var users = await _db.CaptureUsers
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Username, ct);

        var sesionIds = sesiones.Select(s => s.Id).ToList();
        var capturasPorSesion = await _db.InventoryCaptures
            .Where(c => sesionIds.Contains(c.SessionId))
            .GroupBy(c => c.SessionId)
            .Select(g => new
            {
                SessionId = g.Key,
                Total = g.Sum(x => x.Quantity),
                UltimaSync = g.Max(x => x.SyncedAt),
            })
            .ToDictionaryAsync(x => x.SessionId, x => x, ct);

        var capturasPorBarcodePorSesion = await _db.InventoryCaptures
            .Where(c => sesionIds.Contains(c.SessionId))
            .GroupBy(c => new { c.SessionId, c.Barcode })
            .Select(g => new { g.Key.SessionId, g.Key.Barcode, Quantity = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);
        var capturasPorSesionLookup = capturasPorBarcodePorSesion
            .GroupBy(x => x.SessionId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Barcode, x => x.Quantity));

        // Último snapshot por (BranchId, InventoryNumber) -- puede haber más de uno si
        // el congelado se recargó, siempre gana el más reciente (mismo criterio que
        // DiferenciaEngine).
        var snapshots = await _db.FrozenInventorySnapshots
            .Where(s => s.CompanyId == companyId)
            .OrderByDescending(s => s.LoadedAt)
            .ToListAsync(ct);
        var ultimoSnapshotPorCombinacion = snapshots
            .GroupBy(s => (s.BranchId, s.InventoryNumber))
            .ToDictionary(g => g.Key, g => g.First());

        var snapshotIds = ultimoSnapshotPorCombinacion.Values.Select(s => s.Id).ToList();
        var lineasCongeladas = await _db.FrozenInventoryLines
            .Where(l => snapshotIds.Contains(l.SnapshotId))
            .ToListAsync(ct);
        var congeladoPorSnapshot = lineasCongeladas
            .GroupBy(l => l.SnapshotId)
            .ToDictionary(g => g.Key, g => new
            {
                Total = g.Sum(x => x.Quantity),
                Monto = g.Sum(x => x.Quantity * (x.UnitCost ?? 0m)),
                PorBarcode = g.GroupBy(x => x.Barcode).ToDictionary(x => x.Key, x => x.Select(y => y.UnitCost).FirstOrDefault(c => c.HasValue) ?? 0m),
            });

        return sesiones.Select(s =>
        {
            var capturaInfo = capturasPorSesion.GetValueOrDefault(s.Id);
            var contado = capturaInfo?.Total ?? 0;
            var ultimaSync = capturaInfo?.UltimaSync;

            int? congelado = null;
            decimal? congeladoMonto = null;
            decimal contadoMonto = 0m;

            if (ultimoSnapshotPorCombinacion.TryGetValue((s.BranchId, s.InventoryNumber), out var snap)
                && congeladoPorSnapshot.TryGetValue(snap.Id, out var resumenCongelado))
            {
                congelado = resumenCongelado.Total;
                congeladoMonto = resumenCongelado.Monto;

                if (capturasPorSesionLookup.TryGetValue(s.Id, out var contadoPorBarcode))
                {
                    foreach (var (barcode, cantidad) in contadoPorBarcode)
                    {
                        var unitCost = resumenCongelado.PorBarcode.GetValueOrDefault(barcode, 0m);
                        contadoMonto += cantidad * unitCost;
                    }
                }
            }

            // Mismo signo que DiferenciaEngine: positivo = sobrante, negativo = faltante.
            int? diferencia = congelado.HasValue ? contado - congelado.Value : null;
            decimal? diferenciaMonto = congeladoMonto.HasValue ? contadoMonto - congeladoMonto.Value : null;

            return new AvanceRowDto(
                s.Id,
                branches.GetValueOrDefault(s.BranchId, "?"),
                s.InventoryNumber,
                users.GetValueOrDefault(s.ResponsibleUserId, "?"),
                s.Status,
                s.StartedAt,
                ultimaSync,
                congelado,
                congeladoMonto,
                contado,
                contadoMonto,
                diferencia,
                diferenciaMonto);
        }).ToList();
    }

    public sealed class CrearTareaInput
    {
        public long? BranchId { get; set; }
        public string? InventoryNumber { get; set; }
        public long? ResponsibleUserId { get; set; }
        public bool ValidateAgainstMaster { get; set; } = true;
    }

    public sealed class CargarCongeladoInput
    {
        public Guid? SessionId { get; set; }
        public IFormFile? Archivo { get; set; }
    }

    public sealed record AvanceRowDto(
        Guid SessionId,
        string BranchName,
        string InventoryNumber,
        string ResponsibleUserName,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset? UltimaSincronizacion,
        int? Congelado,
        decimal? CongeladoMonto,
        int Contado,
        decimal ContadoMonto,
        int? Diferencia,
        decimal? DiferenciaMonto);
}
