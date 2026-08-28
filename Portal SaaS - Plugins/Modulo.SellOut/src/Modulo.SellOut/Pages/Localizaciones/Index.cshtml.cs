using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Modulo.SellOut.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.SellOut.Pages.Localizaciones;

/// <summary>Mantenedor de dbo.Localizacion -- depende de Geografia (FK logica, sin constraint en la base).</summary>
public class IndexModel : PageModelBaseSellOut
{
    private readonly SellOutDbContext _db;

    public IndexModel(SellOutDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    protected override string CodigoMenu => "localizaciones";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? FiltroTexto { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? FiltroIdGeografia { get; set; }

    public IReadOnlyList<LocalizacionFila> Localizaciones { get; set; } = Array.Empty<LocalizacionFila>();
    public List<SelectListItem> GeografiasDisponibles { get; set; } = new();
    public int? EditandoId { get; set; }
    public bool PuedeVer { get; set; }
    public bool PuedeCrear { get; set; }
    public bool PuedeEditar { get; set; }
    public bool PuedeEliminar { get; set; }

    public record LocalizacionFila(int IDLocalizacion, string? Ciudad, string? CodigoComuna, string? Comuna, int? IDGeografia, string? Region);

    public class InputModel
    {
        [Required]
        public int IDLocalizacion { get; set; }

        public string? Ciudad { get; set; }
        public string? CodigoComuna { get; set; }
        public string? Comuna { get; set; }
        public int? IDGeografia { get; set; }
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
            var localizacion = await _db.Localizaciones.FindAsync(id);
            if (localizacion is not null)
            {
                Input = new InputModel
                {
                    IDLocalizacion = localizacion.IDLocalizacion,
                    Ciudad = localizacion.Ciudad,
                    CodigoComuna = localizacion.CodigoComuna,
                    Comuna = localizacion.Comuna,
                    IDGeografia = localizacion.IDGeografia,
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
                var localizacion = await _db.Localizaciones.FindAsync(id) ?? throw new InvalidOperationException("La localización no existe.");
                localizacion.Ciudad = Input.Ciudad;
                localizacion.CodigoComuna = Input.CodigoComuna;
                localizacion.Comuna = Input.Comuna;
                localizacion.IDGeografia = Input.IDGeografia;
                MensajeExito = "Localización actualizada correctamente.";
            }
            else
            {
                if (await _db.Localizaciones.AnyAsync(l => l.IDLocalizacion == Input.IDLocalizacion))
                {
                    throw new InvalidOperationException($"Ya existe una localización con el ID {Input.IDLocalizacion}.");
                }

                _db.Localizaciones.Add(new Models.Localizacion
                {
                    IDLocalizacion = Input.IDLocalizacion,
                    Ciudad = Input.Ciudad,
                    CodigoComuna = Input.CodigoComuna,
                    Comuna = Input.Comuna,
                    IDGeografia = Input.IDGeografia,
                });
                MensajeExito = "Localización creada correctamente.";
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

        return RedirectToPage(new { filtroTexto = FiltroTexto, filtroIdGeografia = FiltroIdGeografia });
    }

    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        if (!await PuedeEliminarAsync())
        {
            return Forbid();
        }

        try
        {
            var localizacion = await _db.Localizaciones.FindAsync(id) ?? throw new InvalidOperationException("La localización no existe.");
            _db.Localizaciones.Remove(localizacion);
            await _db.SaveChangesAsync();
            MensajeExito = "Localización eliminada.";
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
        }

        return RedirectToPage(new { filtroTexto = FiltroTexto, filtroIdGeografia = FiltroIdGeografia });
    }

    private async Task CargarListasAsync()
    {
        var geografias = await _db.Geografias.OrderBy(g => g.Region).ToListAsync();
        GeografiasDisponibles = geografias
            .Select(g => new SelectListItem($"{g.Region} ({g.RegionCorto})", g.IDGeografia.ToString()))
            .ToList();

        var regionPorGeografia = geografias.ToDictionary(g => g.IDGeografia, g => g.Region);

        var query = _db.Localizaciones.AsQueryable();
        if (FiltroIdGeografia is { } idGeografia)
        {
            query = query.Where(l => l.IDGeografia == idGeografia);
        }
        if (!string.IsNullOrWhiteSpace(FiltroTexto))
        {
            var texto = FiltroTexto.Trim();
            query = query.Where(l => (l.Ciudad != null && EF.Functions.Like(l.Ciudad, $"%{texto}%"))
                || (l.Comuna != null && EF.Functions.Like(l.Comuna, $"%{texto}%")));
        }

        // Join del lado cliente a proposito -- EF Core no puede traducir un lookup contra
        // una lista ya materializada (geografias) dentro de una query IQueryable.
        var localizaciones = await query.OrderBy(l => l.Ciudad).ToListAsync();
        Localizaciones = localizaciones
            .Select(l => new LocalizacionFila(l.IDLocalizacion, l.Ciudad, l.CodigoComuna, l.Comuna, l.IDGeografia,
                l.IDGeografia is { } idGeografiaFila && regionPorGeografia.TryGetValue(idGeografiaFila, out var region) ? region : null))
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
