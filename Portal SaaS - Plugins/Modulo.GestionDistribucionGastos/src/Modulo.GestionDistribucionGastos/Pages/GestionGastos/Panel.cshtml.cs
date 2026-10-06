using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos;

public class PanelModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;

    public PanelModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    public List<CierreMes> MesesDisponibles { get; set; } = new();
    public DashboardViewModel Datos { get; set; } = new();
    public bool HaySeleccion => !string.IsNullOrEmpty(Datos.AnioMes);
    public bool MesCerrado => Datos.EstadoMes == "CERRADO";
    public string? FechaCierre { get; set; }
    public string? UsuarioCierre { get; set; }

    // Cerrar/reabrir el período congela o libera TODO el mes (resolución, aprobación, reglas,
    // recarga y eliminación) -- mismo permiso APPROVE que aprobar/reabrir una cuenta puntual.
    public bool PuedeCerrar { get; set; }

    public bool SoloAbiertos { get; set; }

    public async Task OnGetAsync(string? anioMes, bool? soloAbiertos)
    {
        // Filtro "Solo abiertos" recordado en cookie: sobrevive a los redirect de los POST
        // (Cerrar/Reabrir/Cargar) que no lo llevan en la URL.
        if (soloAbiertos.HasValue)
            Response.Cookies.Append("GDG_SoloAbiertos", soloAbiertos.Value ? "1" : "0", new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(60), IsEssential = true });
        SoloAbiertos = soloAbiertos ?? Request.Cookies["GDG_SoloAbiertos"] == "1";

        // El combo se arma con los meses que realmente existen en Cierre_Mes (no un año fijo hardcodeado),
        // así refleja el estado real y no deja meses cargados fuera de alcance (ej. fuera del año "de trabajo").
        // Con el filtro activo se ocultan los CERRADO, salvo el mes seleccionado (para no perderlo del combo).
        MesesDisponibles = await _db.CierreMes
            .Where(c => !SoloAbiertos || c.Estado != "CERRADO" || c.AnioMes == anioMes)
            .OrderByDescending(c => c.AnioMes)
            .ToListAsync();

        // Sin mes seleccionado por defecto: el usuario debe elegirlo explícitamente en el combo.
        if (string.IsNullOrEmpty(anioMes))
        {
            Datos = new DashboardViewModel { AnioMes = "" };
            return;
        }

        // Período de trabajo persistido en cookie, para que no se pierda al navegar a otras pestañas.
        Response.Cookies.Append("GDG_PeriodoTrabajo", anioMes, new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(60), IsEssential = true });

        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
        FechaCierre = cierre?.FechaCierre?.ToString("dd-MM-yyyy HH:mm");
        UsuarioCierre = cierre?.UsuarioCierre;
        PuedeCerrar = await TieneAccionAsync(PortalActions.Approve);

        var totales = await _db.DistribucionFinal
            .Where(d => d.AnioMes == anioMes)
            .GroupBy(d => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Auto = g.Count(x => x.TipoOrigen == "AUTO"),
                Manual = g.Count(x => x.TipoOrigen == "MANUAL"),
                Pendiente = g.Count(x => x.Estado == "PENDIENTE")
            })
            .FirstOrDefaultAsync();

        Datos = new DashboardViewModel
        {
            AnioMes = anioMes,
            EstadoMes = cierre?.Estado ?? "SIN_CARGAR",
            LineasTotales = totales?.Total ?? 0,
            ResueltasAuto = totales?.Auto ?? 0,
            ResueltasManual = totales?.Manual ?? 0,
            Pendientes = totales?.Pendiente ?? 0
        };
    }

    public async Task<IActionResult> OnPostCargarMesAsync(int anio, int mes)
    {
        var anioMes = $"{anio}{mes:D2}";

        // La recarga REEMPLAZA el mes completo: un mes cerrado no se puede pisar.
        if (await EstaCerradoAsync(anioMes))
        {
            MensajeError = $"El mes {anioMes} está cerrado: no se puede recargar. Reábrelo primero.";
            return RedirectToPage(new { anioMes });
        }

        var conn = _db.Database.GetDbConnection();

        try
        {
            await conn.OpenAsync();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "dbo.sp_CargarCentralizacionMes";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 300;
                cmd.Parameters.Add(new SqlParameter("@Anio", anio));
                cmd.Parameters.Add(new SqlParameter("@Mes", mes));
                await cmd.ExecuteNonQueryAsync();
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "dbo.sp_EjecutarDistribucionAutomatica";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 300;
                cmd.Parameters.Add(new SqlParameter("@AnioMes", anioMes));
                await cmd.ExecuteNonQueryAsync();
            }

            var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
            if (cierre == null)
            {
                _db.CierreMes.Add(new CierreMes { AnioMes = anioMes, Estado = "EN_PROCESO", FechaCarga = DateTime.Now });
            }
            else
            {
                cierre.Estado = "EN_PROCESO";
                cierre.FechaCarga = DateTime.Now;
            }
            await _db.SaveChangesAsync();

            MensajeExito = $"Mes {anioMes} cargado y distribución automática ejecutada.";
        }
        catch (Exception ex)
        {
            MensajeError = $"Error al cargar el mes: {ex.Message}";
        }
        finally
        {
            await conn.CloseAsync();
        }

        return RedirectToPage(new { anioMes });
    }

    public async Task<IActionResult> OnPostCerrarMesAsync(string anioMes, bool confirmarPendientes = false)
    {
        if (!await TieneAccionAsync(PortalActions.Approve))
        {
            MensajeError = "No tienes permiso para cerrar el período.";
            return RedirectToPage(new { anioMes });
        }

        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
        if (cierre == null)
        {
            MensajeError = $"El mes {anioMes} no está cargado.";
            return RedirectToPage(new { anioMes });
        }
        if (cierre.Estado == "CERRADO")
        {
            MensajeError = $"El mes {anioMes} ya está cerrado.";
            return RedirectToPage(new { anioMes });
        }

        // Se permite cerrar con líneas pendientes (quedan congeladas tal cual), pero solo si el
        // usuario lo confirmó explícitamente -- el formulario manda confirmarPendientes=true.
        var pendientes = await _db.DistribucionFinal.CountAsync(d => d.AnioMes == anioMes && d.Estado == "PENDIENTE");
        if (pendientes > 0 && !confirmarPendientes)
        {
            MensajeError = $"El mes {anioMes} tiene {pendientes:N0} línea(s) pendiente(s): confirma el cierre con pendientes.";
            return RedirectToPage(new { anioMes });
        }

        // El cierre se marca directo en Cierre_Mes (el candado que leen todas las pantallas).
        // No se usa dbo.sp_CerrarMes: existe en CLDEPORFIN pero no está versionado en Sql/ y
        // rechaza cualquier mes con pendientes, contra la regla de cierre confirmado de arriba.
        cierre.Estado = "CERRADO";
        cierre.FechaCierre = DateTime.Now;
        cierre.UsuarioCierre = NombreUsuarioActual;

        try
        {
            await _db.SaveChangesAsync();
            MensajeExito = pendientes > 0
                ? $"Mes {anioMes} cerrado con {pendientes:N0} línea(s) pendiente(s) congeladas: ya no admite modificaciones."
                : $"Mes {anioMes} cerrado: ya no admite modificaciones.";
        }
        catch (Exception ex)
        {
            MensajeError = $"No se pudo cerrar el mes: {ex.Message}";
        }

        return RedirectToPage(new { anioMes });
    }

    // Contracara de CerrarMes: devuelve el mes a EN_PROCESO para permitir correcciones.
    // Las cuentas aprobadas (Cuenta_Aprobada) siguen aprobadas -- se reabren una a una si hace falta.
    public async Task<IActionResult> OnPostReabrirMesAsync(string anioMes)
    {
        if (!await TieneAccionAsync(PortalActions.Approve))
        {
            MensajeError = "No tienes permiso para reabrir el período.";
            return RedirectToPage(new { anioMes });
        }

        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
        if (cierre?.Estado != "CERRADO")
        {
            MensajeError = $"El mes {anioMes} no está cerrado.";
            return RedirectToPage(new { anioMes });
        }

        cierre.Estado = "EN_PROCESO";
        cierre.FechaCierre = null;
        cierre.UsuarioCierre = null;
        await _db.SaveChangesAsync();

        MensajeExito = $"Mes {anioMes} reabierto: vuelve a admitir cambios.";
        return RedirectToPage(new { anioMes });
    }

    private async Task<bool> EstaCerradoAsync(string anioMes) =>
        await _db.CierreMes.AnyAsync(c => c.AnioMes == anioMes && c.Estado == "CERRADO");

    public async Task<IActionResult> OnPostEliminarMesAsync(string anioMes)
    {
        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
        if (cierre?.Estado == "CERRADO")
        {
            MensajeError = "El mes está cerrado: no se puede eliminar.";
            return RedirectToPage(new { anioMes });
        }

        // Borra el mes completo (staging, resultado, bloqueos y el registro de Cierre_Mes) para poder
        // recargarlo desde cero. Solo permitido mientras el mes no esté cerrado.
        await _db.DistribucionFinal.Where(d => d.AnioMes == anioMes).ExecuteDeleteAsync();
        await _db.StagingCentralizacion.Where(s => s.AnioMes == anioMes).ExecuteDeleteAsync();
        await _db.CuentaEnTrabajo.Where(b => b.AnioMes == anioMes).ExecuteDeleteAsync();
        if (cierre != null)
        {
            _db.CierreMes.Remove(cierre);
            await _db.SaveChangesAsync();
        }

        MensajeExito = $"Se eliminó todo el registro del mes {anioMes}.";
        return RedirectToPage();
    }
}
