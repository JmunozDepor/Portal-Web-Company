using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;

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

    public async Task OnGetAsync(string? anioMes)
    {
        // El combo se arma con los meses que realmente existen en Cierre_Mes (no un año fijo hardcodeado),
        // así refleja el estado real y no deja meses cargados fuera de alcance (ej. fuera del año "de trabajo").
        MesesDisponibles = await _db.CierreMes.OrderByDescending(c => c.AnioMes).ToListAsync();

        // Sin mes seleccionado por defecto: el usuario debe elegirlo explícitamente en el combo.
        if (string.IsNullOrEmpty(anioMes))
        {
            Datos = new DashboardViewModel { AnioMes = "" };
            return;
        }

        // Período de trabajo persistido en cookie, para que no se pierda al navegar a otras pestañas.
        Response.Cookies.Append("GDG_PeriodoTrabajo", anioMes, new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(60), IsEssential = true });

        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);

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

    public async Task<IActionResult> OnPostCerrarMesAsync(string anioMes)
    {
        var usuario = NombreUsuarioActual;
        var conn = _db.Database.GetDbConnection();

        try
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "dbo.sp_CerrarMes";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add(new SqlParameter("@AnioMes", anioMes));
            cmd.Parameters.Add(new SqlParameter("@Usuario", usuario));
            await cmd.ExecuteNonQueryAsync();

            MensajeExito = $"Mes {anioMes} cerrado correctamente.";
        }
        catch (Exception ex)
        {
            MensajeError = $"No se pudo cerrar el mes: {ex.Message}";
        }
        finally
        {
            await conn.CloseAsync();
        }

        return RedirectToPage(new { anioMes });
    }

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
