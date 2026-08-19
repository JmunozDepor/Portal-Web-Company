// Pages/GestionGastos/Clasificaciones/Form.cshtml.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos.Clasificaciones;

public class FormModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;

    public FormModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    [BindProperty]
    public ClasificacionCuenta Clasificacion { get; set; } = new();

    public bool EsNuevo { get; set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        EsNuevo = id is null;
        if (id is not null)
        {
            var clasificacion = await _db.ClasificacionCuenta.FindAsync(id.Value);
            if (clasificacion is null)
                return NotFound();
            Clasificacion = clasificacion;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync(int? id)
    {
        EsNuevo = id is null;

        if (!ModelState.IsValid)
            return Page();

        bool codigoDuplicado = await _db.ClasificacionCuenta
            .AnyAsync(c => c.Codigo == Clasificacion.Codigo && c.Id != Clasificacion.Id);
        if (codigoDuplicado)
        {
            ModelState.AddModelError(nameof(Clasificacion.Codigo), "Ya existe otra clasificación con este código.");
            return Page();
        }

        try
        {
            if (EsNuevo)
            {
                _db.ClasificacionCuenta.Add(Clasificacion);
                await _db.SaveChangesAsync();
                MensajeExito = "Clasificación creada correctamente.";
            }
            else
            {
                _db.Entry(Clasificacion).State = EntityState.Modified;
                await _db.SaveChangesAsync();
                MensajeExito = "Clasificación actualizada.";
            }
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ObtenerMensajeError(ex));
            return Page();
        }

        return RedirectToPage("/GestionGastos/Clasificaciones/Index");
    }
}
