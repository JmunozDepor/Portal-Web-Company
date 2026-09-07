using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos.Reglas;

public class IndexModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    public ReglaResumenViewModel Datos { get; set; } = new();

    public async Task OnGetAsync()
    {
        var reglas = await _db.ReglasDistribucion
            .OrderByDescending(r => r.Activo)
            .ThenBy(r => r.NroCuenta)
            .ToListAsync();

        var vm = new ReglaResumenViewModel
        {
            TotalReglas = reglas.Count,
            ReglasActivas = reglas.Count(r => r.Activo),
            ReglasInactivas = reglas.Count(r => !r.Activo),
        };
        foreach (var r in reglas)
        {
            vm.Reglas.Add(new ReglaListItemViewModel
            {
                Regla = r,
                CuentasQueMatchea = await ContarCuentasQueMatchean(r.NroCuenta, r.CodCentroCosto),
            });
        }

        Datos = vm;
    }

    // Cuenta cuántas cuentas reales (de todo lo que se cargó alguna vez, no solo un mes) matchea el
    // patrón de una regla -- NroCuenta puede ser un patrón LIKE (ej. "61-09-%"), no solo una cuenta exacta.
    internal async Task<int> ContarCuentasQueMatchean(string patron, string? codCentroCosto)
    {
        var conn = _db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(DISTINCT NroCuenta)
                FROM (SELECT DISTINCT NroCuenta, CodCentroCosto FROM Staging_CentralizacionContable WHERE TipoRegistro = 'DETALLE') u
                WHERE u.NroCuenta LIKE @patron AND (@cc IS NULL OR u.CodCentroCosto = @cc)";
            cmd.Parameters.Add(new SqlParameter("@patron", patron));
            cmd.Parameters.Add(new SqlParameter("@cc", (object?)codCentroCosto ?? DBNull.Value));
            var resultado = await cmd.ExecuteScalarAsync();
            return resultado is int i ? i : 0;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    public async Task<IActionResult> OnPostToggleActivoAsync(int id)
    {
        var regla = await _db.ReglasDistribucion.FindAsync(id);
        if (regla == null)
            return RedirectToPage();

        // Al reactivar (no al desactivar) hay que volver a chequear que no quede superpuesta con otra activa.
        if (!regla.Activo && await FormModel.HayConflicto(_db, regla))
        {
            MensajeError = "No se puede activar: ya existe otra regla activa para esta cuenta (con el mismo centro de costo, o sin especificarlo).";
            return RedirectToPage();
        }

        regla.Activo = !regla.Activo;
        await _db.SaveChangesAsync();

        // Al desactivar, las líneas que el motor automático ya había repartido a nombre de esta
        // regla (TipoOrigen='AUTO', ReglaAplicada='Regla #N') quedaban huérfanas -- la regla ya no
        // estaba activa pero el reparto seguía "aplicado" (bug real en producción). Revertirlas a
        // SIN_AJUSTE deja los registros como estaban antes de que la regla se aplicara.
        var sufijoRevertido = regla.Activo ? "" : await FormModel.RevertirSiCorresponde(_db, regla.Id);
        MensajeExito = (regla.Activo ? "Regla activada." : "Regla desactivada.") + await FormModel.AplicarSiCorresponde(_db, regla) + sufijoRevertido;
        return RedirectToPage();
    }

    // Elimina la regla por completo (a diferencia de desactivar, que solo la apaga). No hay FK que
    // la referencie -- Distribucion_Final.ReglaAplicada es solo texto descriptivo ("Regla #N"), no una
    // relación real -- así que borrar no rompe nada del lado de la base. Sí revierte las líneas ya
    // distribuidas con esta regla (ver RevertirSiCorresponde) -- antes quedaban huérfanas con
    // ReglaAplicada apuntando a un Id que ya no existe (bug real en producción).
    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        var regla = await _db.ReglasDistribucion.FindAsync(id);
        if (regla == null)
            return RedirectToPage();

        var sufijoRevertido = await FormModel.RevertirSiCorresponde(_db, id);

        _db.ReglasDistribucion.Remove(regla);
        await _db.SaveChangesAsync();

        MensajeExito = $"Regla de {regla.NroCuenta} eliminada." + sufijoRevertido;
        return RedirectToPage();
    }
}
