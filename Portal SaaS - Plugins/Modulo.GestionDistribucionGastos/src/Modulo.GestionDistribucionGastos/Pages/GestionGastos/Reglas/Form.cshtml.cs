using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos.Reglas;

// Create+Edit combinados en una sola pagina (mismo patron que Modulo.Administracion
// Usuarios/Editar.cshtml): un id? de ruta ausente = alta nueva.
public class FormModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;

    public FormModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    [BindProperty]
    public ReglaDistribucion Regla { get; set; } = new();

    public bool EsNuevo { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        EsNuevo = id is null;
        if (id is not null)
        {
            var regla = await _db.ReglasDistribucion.FindAsync(id.Value);
            if (regla is null)
                return NotFound();
            Regla = regla;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync(int? id)
    {
        EsNuevo = id is null;

        if (!ModelState.IsValid)
            return Page();

        if (Regla.Activo && await HayConflicto(_db, Regla))
        {
            ModelState.AddModelError(nameof(Regla.NroCuenta),
                "Ya existe otra regla activa para esta cuenta (con el mismo centro de costo, o sin especificarlo). " +
                "Dos reglas activas superpuestas duplicarían el gasto al distribuir — desactivá la otra regla primero.");
            return Page();
        }

        string sufijoMensaje;
        try
        {
            if (EsNuevo)
            {
                Regla.UsuarioCreacion = NombreUsuarioActual;
                Regla.FechaCreacion = DateTime.Now;
                _db.ReglasDistribucion.Add(Regla);
                await _db.SaveChangesAsync();
                sufijoMensaje = "Regla creada correctamente." + await AplicarSiCorresponde(_db, Regla);
            }
            else
            {
                _db.Entry(Regla).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                sufijoMensaje = "Regla actualizada." + await AplicarSiCorresponde(_db, Regla);
            }
        }
        catch (Exception ex)
        {
            // Un error de base al guardar (ej. un valor de BaseDistribucion mas largo que la
            // columna, ver Sql/Ampliar_BaseDistribucion_Reglas.sql) no debe mandar a la
            // pantalla de error generica del Host perdiendo lo que el usuario tipeo -- se
            // muestra en la misma pagina, igual que el chequeo de HayConflicto de mas arriba.
            ModelState.AddModelError(string.Empty, ObtenerMensajeError(ex));
            return Page();
        }

        MensajeExito = sufijoMensaje;
        return RedirectToPage("/GestionGastos/Reglas/Index");
    }

    // NroCuenta puede ser un patrón LIKE (ej. "61-09-%"), así que dos reglas con texto distinto
    // pueden superponerse en la práctica sobre una cuenta real que la comparación de texto no vería
    // (ej. "61-09-%" y "61-09-001-01"). Por eso el chequeo pregunta directo contra las cuentas reales
    // de Staging_CentralizacionContable: ¿existe alguna cuenta que matchee tanto el patrón nuevo como
    // el de alguna otra regla activa? Mismo criterio de "CC null = toda la cuenta" que ya usa el SP.
    // Publico y estatico para que Reglas/Index (ToggleActivo) tambien lo use sin duplicar la query.
    public static async Task<bool> HayConflicto(ApplicationDbContext db, ReglaDistribucion regla)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                ;WITH Cuentas AS (
                    SELECT DISTINCT NroCuenta, CodCentroCosto FROM Staging_CentralizacionContable WHERE TipoRegistro = 'DETALLE'
                )
                SELECT TOP 1 1
                FROM Cuentas c
                WHERE c.NroCuenta LIKE @patronNuevo
                  AND (@ccNuevo IS NULL OR c.CodCentroCosto = @ccNuevo)
                  AND EXISTS (
                      SELECT 1 FROM Reglas_Distribucion r
                      WHERE r.Activo = 1 AND r.Id <> @idExcluir
                        AND c.NroCuenta LIKE r.NroCuenta
                        AND (r.CodCentroCosto IS NULL OR r.CodCentroCosto = c.CodCentroCosto)
                  )";
            cmd.Parameters.Add(new SqlParameter("@patronNuevo", regla.NroCuenta));
            cmd.Parameters.Add(new SqlParameter("@ccNuevo", (object?)regla.CodCentroCosto ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@idExcluir", regla.Id));
            var resultado = await cmd.ExecuteScalarAsync();
            return resultado != null;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    // Una regla recién creada/editada/activada no sirve de nada hasta que se vuelva a correr el motor
    // automático: antes solo se disparaba al "Cargar mes" desde SAP. Ahora, si la regla queda activa,
    // se reaplica de una sobre todos los meses ya cargados que no estén cerrados (idempotente: solo
    // toca líneas que sigan SIN_AJUSTE). Devuelve un sufijo de mensaje listo para el TempData.
    public static async Task<string> AplicarSiCorresponde(ApplicationDbContext db, ReglaDistribucion regla)
    {
        if (!regla.Activo)
            return "";

        var mesesAbiertos = await db.CierreMes
            .Where(c => c.Estado != "CERRADO")
            .Select(c => c.AnioMes)
            .ToListAsync();

        if (mesesAbiertos.Count == 0)
            return "";

        var conn = db.Database.GetDbConnection();
        try
        {
            await conn.OpenAsync();
            foreach (var anioMes in mesesAbiertos)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "dbo.sp_EjecutarDistribucionAutomatica";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 300;
                cmd.Parameters.Add(new SqlParameter("@AnioMes", anioMes));
                await cmd.ExecuteNonQueryAsync();
            }
            return $" Se aplicó a los {mesesAbiertos.Count} mes(es) abierto(s): {string.Join(", ", mesesAbiertos)}.";
        }
        catch (Exception ex)
        {
            return $" ATENCIÓN: no se pudo aplicar a los meses abiertos ({ex.Message}).";
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    // Contracara de AplicarSiCorresponde: al desactivar o eliminar una regla, las líneas que
    // sp_EjecutarDistribucionAutomatica ya había repartido a su nombre (TipoOrigen='AUTO',
    // ReglaAplicada='Regla #{id}') quedaban huérfanas -- la regla ya no existía/estaba inactiva
    // pero el reparto seguía "aplicado" para siempre (bug real en producción). Colapsa cada
    // StagingId afectado de vuelta a una única fila SIN_AJUSTE con los valores originales del
    // staging, mismo criterio exacto que OnPostDeshacerDivisionAsync (CuentasPendientes/Detalle)
    // usa para deshacer una división manual. Respeta los mismos candados que el resto del módulo:
    // no toca meses CERRADO ni cuentas ya aprobadas (Cuenta_Aprobada) -- ahí el usuario tiene que
    // reabrir/reabrir el mes primero, igual que para cualquier otro ajuste manual.
    public static async Task<string> RevertirSiCorresponde(ApplicationDbContext db, int reglaId)
    {
        var etiqueta = $"Regla #{reglaId}";

        var filasAuto = await db.DistribucionFinal
            .Where(d => d.TipoOrigen == "AUTO" && d.ReglaAplicada == etiqueta)
            .ToListAsync();

        if (filasAuto.Count == 0)
            return "";

        var mesesCerrados = (await db.CierreMes
            .Where(c => c.Estado == "CERRADO")
            .Select(c => c.AnioMes)
            .ToListAsync()).ToHashSet();

        var cuentasAprobadas = (await db.CuentaAprobada.ToListAsync())
            .Select(a => (a.AnioMes, a.NroCuenta))
            .ToHashSet();

        int revertidas = 0, omitidasCerrado = 0, omitidasAprobada = 0;

        foreach (var grupo in filasAuto.GroupBy(f => f.StagingId))
        {
            var muestra = grupo.First();
            if (mesesCerrados.Contains(muestra.AnioMes)) { omitidasCerrado++; continue; }
            if (cuentasAprobadas.Contains((muestra.AnioMes, muestra.NroCuenta ?? ""))) { omitidasAprobada++; continue; }

            var staging = await db.StagingCentralizacion.FindAsync(muestra.StagingId);
            if (staging is null) continue;

            db.DistribucionFinal.RemoveRange(grupo);
            db.DistribucionFinal.Add(new DistribucionFinal
            {
                StagingId = staging.Id,
                NroAsiento = staging.NroAsiento,
                LineaId = staging.LineaId,
                AnioMes = staging.AnioMes,
                Fecha = staging.Fecha,
                NroCuenta = staging.NroCuenta,
                NombreCuenta = staging.NombreCuenta,
                CodCentroCostoOriginal = staging.CodCentroCosto,
                CentroCostoOriginal = staging.CentroCosto,
                CodCanalOriginal = staging.CodCanal,
                CanalOriginal = staging.Canal,
                CodSucursalOriginal = staging.CodSucursal,
                SucursalOriginal = staging.Sucursal,
                MontoOriginal = staging.MontoNeto,
                CodCentroCostoDestino = staging.CodCentroCosto,
                CentroCostoDestino = staging.CentroCosto,
                CodCanalDestino = staging.CodCanal,
                CanalDestino = staging.Canal,
                CodSucursalDestino = staging.CodSucursal,
                SucursalDestino = staging.Sucursal,
                MontoDistribuido = staging.MontoNeto,
                TipoOrigen = "SIN_AJUSTE",
                Estado = "PENDIENTE",
            });
            revertidas++;
        }

        if (revertidas > 0)
            await db.SaveChangesAsync();

        var msg = revertidas > 0 ? $" Se revirtió el reparto de {revertidas} línea(s)." : "";
        if (omitidasCerrado > 0)
            msg += $" {omitidasCerrado} línea(s) no se revirtieron por pertenecer a un mes cerrado.";
        if (omitidasAprobada > 0)
            msg += $" {omitidasAprobada} línea(s) no se revirtieron por pertenecer a una cuenta ya aprobada.";
        return msg;
    }
}
