using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos.Reporte;

public class IndexModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    public bool SinPeriodo { get; set; }
    public string AnioMes { get; set; } = "";
    public string? NroCuentaFiltro { get; set; }
    public string? CodCanalFiltro { get; set; }
    public decimal MontoOriginalTotal { get; set; }
    public decimal MontoAjustadoTotal { get; set; }
    public List<string?> Cuentas { get; set; } = new();
    public List<string?> Canales { get; set; } = new();
    public List<ReporteFilaViewModel> Filas { get; set; } = new();

    public async Task OnGetAsync(string? anioMes, string? nroCuenta, string? codCanal)
    {
        anioMes ??= Request.Cookies["GDG_PeriodoTrabajo"];

        if (string.IsNullOrEmpty(anioMes))
        {
            SinPeriodo = true;
            return;
        }

        Response.Cookies.Append("GDG_PeriodoTrabajo", anioMes, new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(60), IsEssential = true });

        var filas = await ObtenerFilasResumenAsync(anioMes, nroCuenta, codCanal);

        AnioMes = anioMes;
        NroCuentaFiltro = nroCuenta;
        CodCanalFiltro = codCanal;
        MontoOriginalTotal = filas.Sum(f => f.MontoOriginal);
        MontoAjustadoTotal = filas.Sum(f => f.MontoDistribuido);
        Cuentas = await _db.DistribucionFinal.Where(d => d.AnioMes == anioMes)
            .Select(d => d.NroCuenta).Distinct().OrderBy(c => c).ToListAsync();
        Canales = await _db.DistribucionFinal.Where(d => d.AnioMes == anioMes)
            .Select(d => d.CodCanalDestino).Distinct().OrderBy(c => c).ToListAsync();
        Filas = filas;
    }

    // Misma agrupacion que se ve en pantalla -- factorizada para que tanto OnGetAsync (la
    // grilla) como OnGetExportarResumenAsync (el Excel "tal como se ve") usen exactamente
    // la misma consulta, sin duplicarla.
    private async Task<List<ReporteFilaViewModel>> ObtenerFilasResumenAsync(string anioMes, string? nroCuenta, string? codCanal)
    {
        var query = _db.DistribucionFinal.Where(d => d.AnioMes == anioMes);

        if (!string.IsNullOrEmpty(nroCuenta))
            query = query.Where(d => d.NroCuenta == nroCuenta);

        if (!string.IsNullOrEmpty(codCanal))
            query = query.Where(d => d.CodCanalDestino == codCanal);

        return await query
            .GroupBy(d => new { d.NroCuenta, d.NombreCuenta, d.CanalDestino, d.SucursalDestino, d.CentroCostoDestino, d.TipoOrigen })
            .Select(g => new ReporteFilaViewModel
            {
                NroCuenta = g.Key.NroCuenta,
                NombreCuenta = g.Key.NombreCuenta,
                CanalDestino = g.Key.CanalDestino,
                SucursalDestino = g.Key.SucursalDestino,
                CentroCostoDestino = g.Key.CentroCostoDestino,
                TipoOrigen = g.Key.TipoOrigen,
                MontoOriginal = g.Sum(x => x.MontoOriginal),
                MontoDistribuido = g.Sum(x => x.MontoDistribuido)
            })
            .OrderBy(f => f.NroCuenta)
            .ToListAsync();
    }

    // GET /gestiongastos/reporte?handler=Exportar&anioMes=202501&nroCuentas=61-01-001-01&nroCuentas=61-02-001-01
    // Detalle linea por linea (todo lo que compone cada asiento) -- sin cuentas marcadas exporta
    // el mes completo (comportamiento de antes); marcando cuentas en la grilla se acota el
    // export a esas cuentas, mucho mas rapido que traer el mes entero cuando solo hace falta
    // revisar el detalle de unas pocas.
    public async Task<IActionResult> OnGetExportarAsync(string anioMes, List<string>? nroCuentas)
    {
        var query = _db.DistribucionFinal.Where(d => d.AnioMes == anioMes);
        if (nroCuentas is { Count: > 0 })
        {
            query = query.Where(d => d.NroCuenta != null && nroCuentas.Contains(d.NroCuenta));
        }

        var filas = await query.ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Distribucion");

        var encabezados = new[]
        {
            "Cuenta", "Nombre cuenta", "Centro costo original", "Canal original", "Sucursal original",
            "Monto original", "Centro costo destino", "Canal destino", "Sucursal destino",
            "Monto distribuido", "Tipo origen", "Usuario", "Fecha", "Comentario"
        };
        for (int i = 0; i < encabezados.Length; i++)
            ws.Cell(1, i + 1).Value = encabezados[i];

        int fila = 2;
        foreach (var d in filas)
        {
            ws.Cell(fila, 1).Value = d.NroCuenta;
            ws.Cell(fila, 2).Value = d.NombreCuenta;
            ws.Cell(fila, 3).Value = d.CentroCostoOriginal;
            ws.Cell(fila, 4).Value = d.CanalOriginal;
            ws.Cell(fila, 5).Value = d.SucursalOriginal;
            ws.Cell(fila, 6).Value = d.MontoOriginal;
            ws.Cell(fila, 7).Value = d.CentroCostoDestino;
            ws.Cell(fila, 8).Value = d.CanalDestino;
            ws.Cell(fila, 9).Value = d.SucursalDestino;
            ws.Cell(fila, 10).Value = d.MontoDistribuido;
            ws.Cell(fila, 11).Value = d.TipoOrigen;
            ws.Cell(fila, 12).Value = d.UsuarioResponsable;
            ws.Cell(fila, 13).Value = d.FechaAsignacion?.ToString("yyyy-MM-dd HH:mm");
            ws.Cell(fila, 14).Value = d.Comentario;
            fila++;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Distribucion_{anioMes}.xlsx");
    }

    // GET /gestiongastos/reporte?handler=ExportarResumen&anioMes=202501
    // Exporta exactamente lo que se ve en la grilla (agrupado por cuenta/canal/sucursal/centro
    // costo/origen, ya sumado) -- no el detalle transaccional, mucho mas liviano.
    public async Task<IActionResult> OnGetExportarResumenAsync(string anioMes, string? nroCuenta, string? codCanal)
    {
        var filas = await ObtenerFilasResumenAsync(anioMes, nroCuenta, codCanal);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Resumen");

        var encabezados = new[] { "Cuenta", "Nombre cuenta", "Canal", "Sucursal", "Centro costo", "Monto original", "Monto ajustado", "Origen" };
        for (int i = 0; i < encabezados.Length; i++)
            ws.Cell(1, i + 1).Value = encabezados[i];

        int fila = 2;
        foreach (var f in filas)
        {
            ws.Cell(fila, 1).Value = f.NroCuenta;
            ws.Cell(fila, 2).Value = f.NombreCuenta;
            ws.Cell(fila, 3).Value = f.CanalDestino;
            ws.Cell(fila, 4).Value = f.SucursalDestino;
            ws.Cell(fila, 5).Value = f.CentroCostoDestino;
            ws.Cell(fila, 6).Value = f.MontoOriginal;
            ws.Cell(fila, 7).Value = f.MontoDistribuido;
            ws.Cell(fila, 8).Value = f.TipoOrigen;
            fila++;
        }

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Resumen_{anioMes}.xlsx");
    }
}
