using System.Data;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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

    // Se llena desde Catalogo_BaseDistribucion (Sql/Catalogo_BaseDistribucion.sql) en vez de
    // tener las <option> hardcodeadas en el Razor -- agregar una base nueva pasa a ser solo un
    // INSERT en esa tabla, sin tocar este código ni el Form.cshtml.
    public List<SelectListItem> OpcionesBaseDistribucion { get; set; } = new();

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

        await CargarOpcionesBaseDistribucionAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync(int? id)
    {
        EsNuevo = id is null;

        if (!ModelState.IsValid)
        {
            await CargarOpcionesBaseDistribucionAsync();
            return Page();
        }

        // HayConflicto (una consulta a la base) quedaba FUERA del try/catch de más abajo -- si
        // esa consulta puntual reventaba por lo que sea, la excepción se escapaba del handler sin
        // pasar por ModelState ni por el resumen de errores (Form.cshtml), y el navegador solo veía
        // un 200 con la página en blanco de mensajes -- exactamente el síntoma reportado al crear
        // una regla con patrón '%' (bug real detectado 2026-08-28, causa todavía sin confirmar).
        // Se mete todo en el mismo try de abajo para que CUALQUIER excepción, venga de donde venga,
        // quede visible en el resumen en vez de desaparecer en silencio.
        string sufijoMensaje;
        try
        {
            if (Regla.Activo && await HayConflicto(_db, Regla))
            {
                ModelState.AddModelError(nameof(Regla.NroCuenta),
                    "Ya existe otra regla activa para esta cuenta (con el mismo centro de costo, o sin especificarlo). " +
                    "Dos reglas activas superpuestas duplicarían el gasto al distribuir — desactivá la otra regla primero.");
                await CargarOpcionesBaseDistribucionAsync();
                return Page();
            }

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
            await CargarOpcionesBaseDistribucionAsync();
            return Page();
        }

        MensajeExito = sufijoMensaje;
        return RedirectToPage("/GestionGastos/Reglas/Index");
    }

    // Lee las bases activas del catálogo (Sql/Catalogo_BaseDistribucion.sql), ordenadas por
    // Orden. Por ADO directo -- igual que HayConflicto/AplicarSiCorresponde más abajo -- porque
    // el catálogo se administra por script SQL, no por migración EF, y no amerita un DbSet propio.
    private async Task CargarOpcionesBaseDistribucionAsync()
    {
        var conn = _db.Database.GetDbConnection();
        var opcionesCargadas = new List<SelectListItem>();
        var estabaAbierta = conn.State == ConnectionState.Open;
        if (!estabaAbierta)
            await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Codigo, Descripcion FROM dbo.Catalogo_BaseDistribucion WHERE Activo = 1 ORDER BY Orden";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                opcionesCargadas.Add(new SelectListItem(reader.GetString(1), reader.GetString(0)));
            }
        }
        finally
        {
            if (!estabaAbierta)
                await conn.CloseAsync();
        }

        OpcionesBaseDistribucion = opcionesCargadas;
    }

    // Bases "...DESDE_IND4_POR_CANAL": sus líneas elegibles (sucursal=IND4 CON canal ya resuelto,
    // ver sp_EjecutarDistribucionAutomatica_Final.sql) nunca se solapan con las de ninguna otra base
    // (todas las demás exigen canal SIN resolver/IND3) -- por eso son las únicas que pueden convivir,
    // activas a la vez, con otra regla activa sobre la misma cuenta, sin duplicar el gasto. Entre
    // ellas mismas SÍ conflictúan (misma condición de elegibilidad -- dos juntas duplicarían la línea).
    private static readonly string[] BasesIndependientesPorCanal =
    {
        "SUCURSAL_DESDE_IND4_POR_CANAL",
        "SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL",
        "VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL",
        "VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL",
        "VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL",
        "VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL",
    };

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
            // Lista de bases "por canal" embebida como literales fijos (no vienen del usuario, son
            // constantes de código -- ver BasesIndependientesPorCanal) para poder usarlas en un IN().
            var listaBasesPorCanal = string.Join(", ", BasesIndependientesPorCanal.Select(b => $"'{b}'"));

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
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
                        -- Excepción: una base '...DESDE_IND4_POR_CANAL' nunca compite por la misma
                        -- línea con ninguna base que NO sea de esa familia (mutuamente excluyentes
                        -- por diseño en el SP), así que combinarlas es seguro -- se omite el
                        -- conflicto solo cuando EXACTAMENTE una de las dos pertenece a la familia
                        -- (dos de la familia entre sí SÍ conflictúan, tienen la misma elegibilidad).
                        AND NOT (
                              (@baseNueva IN ({listaBasesPorCanal}) AND r.BaseDistribucion NOT IN ({listaBasesPorCanal}))
                           OR (r.BaseDistribucion IN ({listaBasesPorCanal}) AND @baseNueva NOT IN ({listaBasesPorCanal}))
                            )
                  )";
            cmd.Parameters.Add(new SqlParameter("@patronNuevo", regla.NroCuenta));
            cmd.Parameters.Add(new SqlParameter("@ccNuevo", (object?)regla.CodCentroCosto ?? DBNull.Value));
            cmd.Parameters.Add(new SqlParameter("@idExcluir", regla.Id));
            cmd.Parameters.Add(new SqlParameter("@baseNueva", regla.BaseDistribucion));
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
