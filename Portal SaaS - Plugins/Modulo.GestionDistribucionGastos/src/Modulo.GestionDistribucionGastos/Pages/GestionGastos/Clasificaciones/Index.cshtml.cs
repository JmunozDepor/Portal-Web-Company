// Pages/GestionGastos/Clasificaciones/Index.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos.Clasificaciones;

public class IndexModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;

    public IndexModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    public List<ClasificacionCuenta> Clasificaciones { get; set; } = new();

    public async Task OnGetAsync()
    {
        Clasificaciones = await _db.ClasificacionCuenta
            .OrderBy(c => c.Orden)
            .ThenBy(c => c.Nombre)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostToggleActivoAsync(int id)
    {
        var clasificacion = await _db.ClasificacionCuenta.FindAsync(id);
        if (clasificacion == null)
            return RedirectToPage();

        clasificacion.Activo = !clasificacion.Activo;
        await _db.SaveChangesAsync();

        MensajeExito = clasificacion.Activo ? "Clasificación activada." : "Clasificación desactivada.";
        return RedirectToPage();
    }

    // No se permite eliminar una clasificación referenciada por Agrupacion_Cuenta -- dejaría
    // filas con ClasificacionId huérfano. Solo desactivar en ese caso.
    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        var clasificacion = await _db.ClasificacionCuenta.FindAsync(id);
        if (clasificacion == null)
            return RedirectToPage();

        bool enUso = await _db.AgrupacionCuenta.AnyAsync(a => a.ClasificacionId == id);
        if (enUso)
        {
            MensajeError = $"No se puede eliminar \"{clasificacion.Nombre}\": hay cuentas asignadas a esta clasificación. Desactívala en vez de eliminarla.";
            return RedirectToPage();
        }

        _db.ClasificacionCuenta.Remove(clasificacion);
        await _db.SaveChangesAsync();

        MensajeExito = $"Clasificación \"{clasificacion.Nombre}\" eliminada.";
        return RedirectToPage();
    }
}
