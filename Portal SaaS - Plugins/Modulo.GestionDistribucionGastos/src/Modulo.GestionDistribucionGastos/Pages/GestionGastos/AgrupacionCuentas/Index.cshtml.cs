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

        await SincronizarUniversoDeCuentasAsync();

        // Trae también clasificaciones inactivas ya asignadas, para no perderlas de la vista
        // (se muestran igual en el dropdown de su fila, aunque no aparezcan para nuevas
        // asignaciones en cuentas sin clasificar).
        ClasificacionesDisponibles = await _db.ClasificacionCuenta
            .Where(c => c.Activo)
            .OrderBy(c => c.Orden)
            .ThenBy(c => c.Nombre)
            .ToListAsync();

        IQueryable<AgrupacionCuenta> query = _db.AgrupacionCuenta;
        if (!string.IsNullOrWhiteSpace(filtro))
            query = query.Where(a => a.NroCuenta.Contains(filtro) || (a.NombreCuenta != null && a.NombreCuenta.Contains(filtro)));
        if (soloSinClasificar)
            query = query.Where(a => a.ClasificacionId == null);

        Cuentas = await query
            .OrderBy(a => a.NroCuenta)
            .Select(a => new FilaAgrupacion(a.NroCuenta, a.NombreCuenta, a.ClasificacionId))
            .ToListAsync();
    }

    // Inserta en Agrupacion_Cuenta las cuentas nuevas encontradas en el universo real
    // (Staging_CentralizacionContable, TipoRegistro='DETALLE' -- mismo criterio que
    // Reglas/Index.cshtml.cs::ContarCuentasQueMatchean) que todavía no existan en la
    // tabla. No modifica ni borra filas existentes: una cuenta ya clasificada no se
    // toca, y una cuenta que dejó de aparecer en el universo real conserva su fila y
    // su clasificación histórica.
    private async Task SincronizarUniversoDeCuentasAsync()
    {
        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                ;WITH Universo AS (
                    SELECT DISTINCT NroCuenta, NombreCuenta
                    FROM dbo.Staging_CentralizacionContable
                    WHERE TipoRegistro = 'DETALLE' AND NroCuenta IS NOT NULL
                )
                MERGE dbo.Agrupacion_Cuenta AS destino
                USING Universo AS origen
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
