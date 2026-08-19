using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos.CuentasPendientes;

public class IndexModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;
    private const int MinutosLiberacionBloqueo = 15;

    public IndexModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    public bool SinPeriodo { get; set; }
    public string AnioMes { get; set; } = "";
    public bool MesCerrado { get; set; }
    public bool PuedeAprobar { get; set; }
    public List<CuentaPendienteViewModel> Cuentas { get; set; } = new();

    // GET ?anioMes=202501
    public async Task OnGetAsync(string? anioMes)
    {
        // Si no viene por query string, se usa el período de trabajo guardado en cookie (elegido antes
        // en "Período") -- para que no se pierda el mes al cambiar de pestaña.
        anioMes ??= Request.Cookies["GDG_PeriodoTrabajo"];

        if (string.IsNullOrEmpty(anioMes))
        {
            SinPeriodo = true;
            return;
        }

        Response.Cookies.Append("GDG_PeriodoTrabajo", anioMes, new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(60), IsEssential = true });

        // Se listan TODAS las cuentas trabajables del mes (pendientes y ya asignadas manualmente),
        // no solo las que aún tienen líneas sin resolver, para poder reabrirlas y corregir si hace falta.
        var cuentas = await _db.DistribucionFinal
            .Where(d => d.AnioMes == anioMes && (d.TipoOrigen == "SIN_AJUSTE" || d.TipoOrigen == "MANUAL"))
            .GroupBy(d => new { d.NroCuenta, d.NombreCuenta })
            .Select(g => new CuentaPendienteViewModel
            {
                NroCuenta = g.Key.NroCuenta ?? "",
                NombreCuenta = g.Key.NombreCuenta,
                LineasTotales = g.Count(),
                LineasPendientes = g.Count(x => x.Estado == "PENDIENTE"),
                MontoPendiente = g.Sum(x => x.Estado == "PENDIENTE" ? x.MontoOriginal : 0),
                LineasIndirectas = g.Count(x => x.EsGastoIndirecto)
            })
            .OrderBy(c => c.NroCuenta)
            .ToListAsync();

        var bloqueos = await _db.CuentaEnTrabajo
            .Where(b => b.AnioMes == anioMes)
            .ToListAsync();

        var aprobadas = await _db.CuentaAprobada
            .Where(a => a.AnioMes == anioMes)
            .ToListAsync();

        foreach (var cuenta in cuentas)
        {
            var bloqueo = bloqueos.FirstOrDefault(b => b.NroCuenta == cuenta.NroCuenta);
            if (bloqueo != null && (DateTime.Now - bloqueo.FechaBloqueo).TotalMinutes <= MinutosLiberacionBloqueo)
            {
                cuenta.TomadaPorOtroUsuario = bloqueo.BloqueadoPor != NombreUsuarioActual;
                cuenta.UsuarioQueLaTiene = bloqueo.BloqueadoPor;
            }

            var aprobada = aprobadas.FirstOrDefault(a => a.NroCuenta == cuenta.NroCuenta);
            if (aprobada != null)
            {
                cuenta.Aprobada = true;
                cuenta.UsuarioAprobador = aprobada.UsuarioAprobador;
                cuenta.FechaAprobacion = aprobada.FechaAprobacion;
            }
        }

        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);

        AnioMes = anioMes;
        MesCerrado = cierre?.Estado == "CERRADO";
        PuedeAprobar = await TieneAccionAsync(PortalActions.Approve);
        Cuentas = cuentas;
    }

    public async Task<IActionResult> OnPostTomarAsync(string anioMes, string nroCuenta)
    {
        var bloqueo = await _db.CuentaEnTrabajo
            .FirstOrDefaultAsync(b => b.AnioMes == anioMes && b.NroCuenta == nroCuenta);

        if (bloqueo != null && (DateTime.Now - bloqueo.FechaBloqueo).TotalMinutes <= MinutosLiberacionBloqueo
            && bloqueo.BloqueadoPor != NombreUsuarioActual)
        {
            MensajeError = $"Esta cuenta ya está siendo trabajada por {bloqueo.BloqueadoPor}.";
            return RedirectToPage("/GestionGastos/CuentasPendientes/Index", new { anioMes });
        }

        if (bloqueo == null)
        {
            _db.CuentaEnTrabajo.Add(new Models.CuentaEnTrabajo
            {
                AnioMes = anioMes,
                NroCuenta = nroCuenta,
                BloqueadoPor = NombreUsuarioActual,
                FechaBloqueo = DateTime.Now
            });
        }
        else
        {
            bloqueo.BloqueadoPor = NombreUsuarioActual;
            bloqueo.FechaBloqueo = DateTime.Now;
        }

        await _db.SaveChangesAsync();
        return RedirectToPage("/GestionGastos/CuentasPendientes/Detalle", new { anioMes, nroCuenta });
    }

    // Toggle "masivo": marca TODAS las líneas de la cuenta/mes de una vez, sin necesidad de
    // abrir Detalle ni renderizar la grilla completa (ver ViewModels.CuentaPendienteViewModel.LineasIndirectas).
    public async Task<IActionResult> OnPostMarcarIndirectoCuentaAsync(string anioMes, string nroCuenta, bool indirecto)
    {
        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
        if (cierre?.Estado == "CERRADO")
        {
            MensajeError = "El mes está cerrado: no se pueden guardar cambios.";
            return RedirectToPage(new { anioMes });
        }

        var bloqueo = await _db.CuentaEnTrabajo
            .FirstOrDefaultAsync(b => b.AnioMes == anioMes && b.NroCuenta == nroCuenta);
        if (bloqueo != null && (DateTime.Now - bloqueo.FechaBloqueo).TotalMinutes <= MinutosLiberacionBloqueo
            && bloqueo.BloqueadoPor != NombreUsuarioActual)
        {
            MensajeError = $"Esta cuenta está bloqueada por {bloqueo.BloqueadoPor}.";
            return RedirectToPage(new { anioMes });
        }

        var afectadas = await _db.DistribucionFinal
            .Where(d => d.AnioMes == anioMes && d.NroCuenta == nroCuenta)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EsGastoIndirecto, indirecto));

        MensajeExito = $"Se marcaron {afectadas} línea(s) de {nroCuenta} como gasto {(indirecto ? "indirecto" : "directo")}.";
        return RedirectToPage(new { anioMes });
    }

    public async Task<IActionResult> OnPostLiberarAsync(string anioMes, string nroCuenta)
    {
        var bloqueo = await _db.CuentaEnTrabajo
            .FirstOrDefaultAsync(b => b.AnioMes == anioMes && b.NroCuenta == nroCuenta && b.BloqueadoPor == NombreUsuarioActual);

        if (bloqueo != null)
        {
            _db.CuentaEnTrabajo.Remove(bloqueo);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage(new { anioMes });
    }

    // A diferencia de OnPostLiberarAsync (que solo suelta tu propio bloqueo), esto rompe el
    // bloqueo de OTRO usuario -- para cuando alguien cerró el navegador sin usar "Liberar
    // cuenta" o su sesión quedó colgada, y nadie más puede tocar la cuenta hasta que expiren
    // los 15 minutos. Gateado con el mismo permiso Aprobar que ya usa Aprobar/Reabrir cuenta,
    // no cualquiera puede sacarle la cuenta a otro mientras la está usando de verdad.
    public async Task<IActionResult> OnPostForzarLiberacionAsync(string anioMes, string nroCuenta)
    {
        if (!await TieneAccionAsync(PortalActions.Approve))
        {
            MensajeError = "No tenés permiso para forzar la liberación de una cuenta bloqueada.";
            return RedirectToPage(new { anioMes });
        }

        var bloqueo = await _db.CuentaEnTrabajo
            .FirstOrDefaultAsync(b => b.AnioMes == anioMes && b.NroCuenta == nroCuenta);
        if (bloqueo != null)
        {
            var usuarioAnterior = bloqueo.BloqueadoPor;
            _db.CuentaEnTrabajo.Remove(bloqueo);
            await _db.SaveChangesAsync();
            MensajeExito = $"Se forzó la liberación de {nroCuenta} (estaba tomada por {usuarioAnterior}).";
        }

        return RedirectToPage(new { anioMes });
    }

    // Para cuentas donde el reparto real ya viene correcto de SAP y no hay nada que redistribuir
    // (ej. Sueldos algunos meses): saca las líneas PENDIENTE de la cola sin asignarles ningún
    // destino nuevo, dejándolas en EN_REVISION -- listas para que un aprobador las revise (ver
    // OnPostAprobarCuentaAsync). Las líneas ya resueltas (APROBADO, vía asignación o división) no
    // se tocan.
    public async Task<IActionResult> OnPostMarcarListaCuentaAsync(string anioMes, string nroCuenta)
    {
        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
        if (cierre?.Estado == "CERRADO")
        {
            MensajeError = "El mes está cerrado: no se pueden guardar cambios.";
            return RedirectToPage(new { anioMes });
        }

        if (await _db.CuentaAprobada.AnyAsync(a => a.AnioMes == anioMes && a.NroCuenta == nroCuenta))
        {
            MensajeError = "La cuenta ya está aprobada: pedile al aprobador que la reabra para modificarla.";
            return RedirectToPage(new { anioMes });
        }

        var bloqueo = await _db.CuentaEnTrabajo
            .FirstOrDefaultAsync(b => b.AnioMes == anioMes && b.NroCuenta == nroCuenta);
        if (bloqueo != null && (DateTime.Now - bloqueo.FechaBloqueo).TotalMinutes <= MinutosLiberacionBloqueo
            && bloqueo.BloqueadoPor != NombreUsuarioActual)
        {
            MensajeError = $"Esta cuenta está bloqueada por {bloqueo.BloqueadoPor}.";
            return RedirectToPage(new { anioMes });
        }

        var afectadas = await _db.DistribucionFinal
            .Where(d => d.AnioMes == anioMes && d.NroCuenta == nroCuenta && d.Estado == "PENDIENTE")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, "EN_REVISION"));

        if (afectadas == 0)
        {
            MensajeError = "No hay líneas pendientes en esta cuenta.";
            return RedirectToPage(new { anioMes });
        }

        MensajeExito = $"Se marcaron {afectadas} línea(s) de {nroCuenta} como sin cambios (listas para aprobar).";
        return RedirectToPage(new { anioMes });
    }

    // Bloqueo duro por cuenta -- distinto del bloqueo transitorio de edición (Cuenta_En_Trabajo,
    // que expira solo a los 15 minutos): mientras exista la fila en Cuenta_Aprobada, ni Detalle ni
    // los handlers de escritura de esta cuenta aceptan cambios (ver DetalleModel).
    public async Task<IActionResult> OnPostAprobarCuentaAsync(string anioMes, string nroCuenta)
    {
        if (!await TieneAccionAsync(PortalActions.Approve))
        {
            MensajeError = "No tenés permiso para aprobar cuentas.";
            return RedirectToPage(new { anioMes });
        }

        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
        if (cierre?.Estado == "CERRADO")
        {
            MensajeError = "El mes está cerrado: no se pueden guardar cambios.";
            return RedirectToPage(new { anioMes });
        }

        var pendientes = await _db.DistribucionFinal
            .CountAsync(d => d.AnioMes == anioMes && d.NroCuenta == nroCuenta && d.Estado == "PENDIENTE");
        if (pendientes > 0)
        {
            MensajeError = $"No se puede aprobar: quedan {pendientes} línea(s) pendientes.";
            return RedirectToPage(new { anioMes });
        }

        if (await _db.CuentaAprobada.AnyAsync(a => a.AnioMes == anioMes && a.NroCuenta == nroCuenta))
        {
            MensajeError = "La cuenta ya estaba aprobada.";
            return RedirectToPage(new { anioMes });
        }

        _db.CuentaAprobada.Add(new CuentaAprobada
        {
            AnioMes = anioMes,
            NroCuenta = nroCuenta,
            UsuarioAprobador = NombreUsuarioActual,
            FechaAprobacion = DateTime.Now
        });
        await _db.SaveChangesAsync();

        MensajeExito = $"Cuenta {nroCuenta} aprobada y bloqueada.";
        return RedirectToPage(new { anioMes });
    }

    public async Task<IActionResult> OnPostReabrirCuentaAsync(string anioMes, string nroCuenta)
    {
        if (!await TieneAccionAsync(PortalActions.Approve))
        {
            MensajeError = "No tenés permiso para reabrir cuentas aprobadas.";
            return RedirectToPage(new { anioMes });
        }

        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
        if (cierre?.Estado == "CERRADO")
        {
            MensajeError = "El mes está cerrado: no se pueden guardar cambios.";
            return RedirectToPage(new { anioMes });
        }

        var aprobada = await _db.CuentaAprobada
            .FirstOrDefaultAsync(a => a.AnioMes == anioMes && a.NroCuenta == nroCuenta);
        if (aprobada != null)
        {
            _db.CuentaAprobada.Remove(aprobada);
            await _db.SaveChangesAsync();
            MensajeExito = $"Cuenta {nroCuenta} reabierta.";
        }

        return RedirectToPage(new { anioMes });
    }
}
