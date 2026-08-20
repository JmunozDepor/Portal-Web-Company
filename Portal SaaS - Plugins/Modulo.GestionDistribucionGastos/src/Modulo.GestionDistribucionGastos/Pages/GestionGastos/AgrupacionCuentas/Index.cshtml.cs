// Pages/GestionGastos/AgrupacionCuentas/Index.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos.AgrupacionCuentas;

public class IndexModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    public record FilaAgrupacion(string NroCuenta, string? NombreCuenta, int? ClasificacionId);

    public List<FilaAgrupacion> Cuentas { get; set; } = new();
    public List<ClasificacionCuenta> ClasificacionesDisponibles { get; set; } = new();
    public string? Filtro { get; set; }
    public bool SoloSinClasificar { get; set; }

    public async Task OnGetAsync(string? filtro, bool soloSinClasificar = false)
    {
        Filtro = filtro;
        SoloSinClasificar = soloSinClasificar;

        try
        {
            await SincronizarUniversoDeCuentasAsync();
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
        }

        IQueryable<AgrupacionCuenta> query = _db.AgrupacionCuenta;
        if (!string.IsNullOrWhiteSpace(filtro))
            query = query.Where(a => a.NroCuenta.Contains(filtro) || (a.NombreCuenta != null && a.NombreCuenta.Contains(filtro)));
        if (soloSinClasificar)
            query = query.Where(a => a.ClasificacionId == null);

        Cuentas = await query
            .OrderBy(a => a.NroCuenta)
            .Select(a => new FilaAgrupacion(a.NroCuenta, a.NombreCuenta, a.ClasificacionId))
            .ToListAsync();

        // Trae las clasificaciones activas (para nuevas asignaciones) más cualquier
        // clasificación inactiva que ya esté asignada a una de las filas mostradas, para
        // no perderla de vista en el dropdown de su fila (evita que se vea/quede como
        // "(sin clasificar)" y se pierda la asignación real ante un guardado accidental).
        var idsAsignados = Cuentas
            .Where(c => c.ClasificacionId != null)
            .Select(c => c.ClasificacionId!.Value)
            .Distinct()
            .ToList();

        ClasificacionesDisponibles = await _db.ClasificacionCuenta
            .Where(c => c.Activo || idsAsignados.Contains(c.Id))
            .OrderBy(c => c.Orden)
            .ThenBy(c => c.Nombre)
            .ToListAsync();
    }

    // Inserta en Agrupacion_Cuenta las cuentas nuevas encontradas en el universo real
    // que todavía no existan en la tabla. El universo se arma con las MISMAS dos
    // fuentes que WITH Datos AS (...) en Sql/vw_EerrAnual.sql, para cubrir los 6
    // grupos completos (4-9), no solo 6-9:
    //   - Distribucion_Final: grupos 6-9 (gastos), ya corregidos por el motor de
    //     distribución.
    //   - Staging_CentralizacionContable TipoRegistro='RESUMEN': grupos 4-5
    //     (Ingresos/Costo de Ventas, que nunca se tocan ni se distribuyen en este
    //     sistema -- ver el comentario de lineas45 en Eerr/Index.cshtml.cs), más
    //     6-9 antes de corregir (se descartan por el MERGE de abajo si ya llegaron
    //     por Distribucion_Final, o se insertan igual si Distribucion_Final aún no
    //     tiene esa cuenta ese mes).
    // (A diferencia de Reglas/Index.cshtml.cs::ContarCuentasQueMatchean, que solo
    // necesita Staging_CentralizacionContable TipoRegistro='DETALLE' porque las
    // reglas de distribución solo aplican a gastos 6-9, acá se necesita el universo
    // completo de cuentas para poder clasificar también Ingresos y Costo de Ventas.)
    // No modifica ni borra filas existentes: una cuenta ya clasificada no se toca, y
    // una cuenta que dejó de aparecer en el universo real conserva su fila y su
    // clasificación histórica.
    private async Task SincronizarUniversoDeCuentasAsync()
    {
        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                ;WITH Universo AS (
                    SELECT NroCuenta, NombreCuenta
                    FROM dbo.Distribucion_Final
                    WHERE NroCuenta IS NOT NULL

                    UNION ALL

                    SELECT NroCuenta, NombreCuenta
                    FROM dbo.Staging_CentralizacionContable
                    WHERE TipoRegistro = 'RESUMEN' AND NroCuenta IS NOT NULL
                ),
                UniversoAgg AS (
                    SELECT NroCuenta, NombreCuenta = MAX(NombreCuenta)
                    FROM Universo
                    GROUP BY NroCuenta
                )
                MERGE dbo.Agrupacion_Cuenta WITH (HOLDLOCK) AS destino
                USING UniversoAgg AS origen
                ON destino.NroCuenta = origen.NroCuenta
                WHEN NOT MATCHED BY TARGET THEN
                    INSERT (NroCuenta, NombreCuenta, ClasificacionId, FechaModificacion, UsuarioModificacion)
                    VALUES (origen.NroCuenta, origen.NombreCuenta, NULL, GETDATE(), NULL);";
            cmd.CommandTimeout = 120;
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<IActionResult> OnPostAsignarAsync(string nroCuenta, int? clasificacionId, string? filtro, bool soloSinClasificar = false)
    {
        var fila = await _db.AgrupacionCuenta.FindAsync(nroCuenta);
        if (fila == null)
        {
            MensajeError = $"La cuenta {nroCuenta} ya no existe en la tabla de agrupación.";
            return RedirectToPage(new { filtro, soloSinClasificar });
        }

        fila.ClasificacionId = clasificacionId;
        fila.FechaModificacion = DateTime.Now;
        fila.UsuarioModificacion = NombreUsuarioActual;
        await _db.SaveChangesAsync();

        MensajeExito = $"Cuenta {nroCuenta} actualizada.";
        return RedirectToPage(new { filtro, soloSinClasificar });
    }
}
