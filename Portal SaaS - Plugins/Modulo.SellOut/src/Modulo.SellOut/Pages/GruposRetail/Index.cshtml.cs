using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Modulo.SellOut.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.SellOut.Pages.GruposRetail;

/// <summary>Mantenedor de dbo.GrupoRetail -- depende de Localizacion (FK logica, sin constraint en la base).</summary>
public class IndexModel : PageModelBaseSellOut
{
    private readonly SellOutDbContext _db;

    public IndexModel(SellOutDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    protected override string CodigoMenu => "gruposretail";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? FiltroTexto { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? FiltroIdLocalizacion { get; set; }

    public IReadOnlyList<GrupoRetailFila> Grupos { get; set; } = Array.Empty<GrupoRetailFila>();
    public List<SelectListItem> LocalizacionesDisponibles { get; set; } = new();
    public int? EditandoId { get; set; }
    public bool PuedeVer { get; set; }
    public bool PuedeCrear { get; set; }
    public bool PuedeEditar { get; set; }
    public bool PuedeEliminar { get; set; }

    public record GrupoRetailFila(int IDGrupoRetail, string? NombreGrupoRetail, string? Propietario, int IDLocalizacion, string? Ciudad);

    public class InputModel
    {
        [Required]
        public int IDGrupoRetail { get; set; }

        public string? NombreGrupoRetail { get; set; }
        public string? Propietario { get; set; }

        [Required]
        public int IDLocalizacion { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int? editando)
    {
        await CargarPermisosAsync();
        if (!PuedeVer)
        {
            return Forbid();
        }

        EditandoId = editando;
        await CargarListasAsync();

        if (editando is { } id)
        {
            var grupo = await _db.GruposRetail.FindAsync(id);
            if (grupo is not null)
            {
                Input = new InputModel
                {
                    IDGrupoRetail = grupo.IDGrupoRetail,
                    NombreGrupoRetail = grupo.NombreGrupoRetail,
                    Propietario = grupo.Propietario,
                    IDLocalizacion = grupo.IDLocalizacion,
                };
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync(int? editando)
    {
        if (!await (editando is null ? PuedeCrearAsync() : PuedeEditarAsync()))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            await CargarPermisosAsync();
            EditandoId = editando;
            await CargarListasAsync();
            return Page();
        }

        try
        {
            if (editando is { } id)
            {
                var grupo = await _db.GruposRetail.FindAsync(id) ?? throw new InvalidOperationException("El grupo retail no existe.");
                grupo.NombreGrupoRetail = Input.NombreGrupoRetail;
                grupo.Propietario = Input.Propietario;
                grupo.IDLocalizacion = Input.IDLocalizacion;
                MensajeExito = "Grupo retail actualizado correctamente.";
            }
            else
            {
                if (await _db.GruposRetail.AnyAsync(g => g.IDGrupoRetail == Input.IDGrupoRetail))
                {
                    throw new InvalidOperationException($"Ya existe un grupo retail con el ID {Input.IDGrupoRetail}.");
                }

                _db.GruposRetail.Add(new Models.GrupoRetail
                {
                    IDGrupoRetail = Input.IDGrupoRetail,
                    NombreGrupoRetail = Input.NombreGrupoRetail,
                    Propietario = Input.Propietario,
                    IDLocalizacion = Input.IDLocalizacion,
                });
                MensajeExito = "Grupo retail creado correctamente.";
            }

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ObtenerMensajeError(ex));
            await CargarPermisosAsync();
            EditandoId = editando;
            await CargarListasAsync();
            return Page();
        }

        return RedirectToPage(new { filtroTexto = FiltroTexto, filtroIdLocalizacion = FiltroIdLocalizacion });
    }

    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        if (!await PuedeEliminarAsync())
        {
            return Forbid();
        }

        try
        {
            var grupo = await _db.GruposRetail.FindAsync(id) ?? throw new InvalidOperationException("El grupo retail no existe.");
            _db.GruposRetail.Remove(grupo);
            await _db.SaveChangesAsync();
            MensajeExito = "Grupo retail eliminado.";
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
        }

        return RedirectToPage(new { filtroTexto = FiltroTexto, filtroIdLocalizacion = FiltroIdLocalizacion });
    }

    private async Task CargarListasAsync()
    {
        var localizaciones = await _db.Localizaciones.OrderBy(l => l.Ciudad).ToListAsync();
        LocalizacionesDisponibles = localizaciones
            .Select(l => new SelectListItem(l.Ciudad ?? $"Localización {l.IDLocalizacion}", l.IDLocalizacion.ToString()))
            .ToList();

        var ciudadPorLocalizacion = localizaciones.ToDictionary(l => l.IDLocalizacion, l => l.Ciudad);

        var query = _db.GruposRetail.AsQueryable();
        if (FiltroIdLocalizacion is { } idLocalizacion)
        {
            query = query.Where(g => g.IDLocalizacion == idLocalizacion);
        }
        if (!string.IsNullOrWhiteSpace(FiltroTexto))
        {
            var texto = FiltroTexto.Trim();
            query = query.Where(g => (g.NombreGrupoRetail != null && EF.Functions.Like(g.NombreGrupoRetail, $"%{texto}%"))
                || (g.Propietario != null && EF.Functions.Like(g.Propietario, $"%{texto}%")));
        }

        var grupos = await query.OrderBy(g => g.NombreGrupoRetail).ToListAsync();
        Grupos = grupos
            .Select(g => new GrupoRetailFila(g.IDGrupoRetail, g.NombreGrupoRetail, g.Propietario, g.IDLocalizacion,
                ciudadPorLocalizacion.TryGetValue(g.IDLocalizacion, out var ciudad) ? ciudad : null))
            .ToList();
    }

    private async Task CargarPermisosAsync()
    {
        PuedeVer = await PuedeVerAsync();
        PuedeCrear = await PuedeCrearAsync();
        PuedeEditar = await PuedeEditarAsync();
        PuedeEliminar = await PuedeEliminarAsync();
    }
}
