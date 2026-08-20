using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.SellOut.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.SellOut.Pages.Geografias;

/// <summary>Mantenedor de dbo.Geografia -- base de la cadena Geografia -> Localizacion -> GrupoRetail/Sucursal.</summary>
public class IndexModel : PageModelBaseSellOut
{
    private readonly SellOutDbContext _db;

    public IndexModel(SellOutDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    protected override string CodigoMenu => "geografias";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? FiltroTexto { get; set; }

    public IReadOnlyList<Models.Geografia> Geografias { get; set; } = Array.Empty<Models.Geografia>();
    public int? EditandoId { get; set; }
    public bool PuedeVer { get; set; }
    public bool PuedeCrear { get; set; }
    public bool PuedeEditar { get; set; }
    public bool PuedeEliminar { get; set; }

    public class InputModel
    {
        [Required]
        public int IDGeografia { get; set; }

        [Required]
        public string Region { get; set; } = string.Empty;

        [Required]
        public string RegionCorto { get; set; } = string.Empty;

        public string? GrupoRegion { get; set; }

        [Required]
        public string CodigoPais { get; set; } = string.Empty;

        [Required]
        public string Pais { get; set; } = string.Empty;

        public string? CodigoPostal { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int? editando)
    {
        await CargarPermisosAsync();
        if (!PuedeVer)
        {
            return Forbid();
        }

        EditandoId = editando;
        await CargarListaAsync();

        if (editando is { } id)
        {
            var geografia = await _db.Geografias.FindAsync(id);
            if (geografia is not null)
            {
                Input = new InputModel
                {
                    IDGeografia = geografia.IDGeografia,
                    Region = geografia.Region,
                    RegionCorto = geografia.RegionCorto,
                    GrupoRegion = geografia.GrupoRegion,
                    CodigoPais = geografia.CodigoPais,
                    Pais = geografia.Pais,
                    CodigoPostal = geografia.CodigoPostal,
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
            await CargarListaAsync();
            return Page();
        }

        try
        {
            if (editando is { } id)
            {
                var geografia = await _db.Geografias.FindAsync(id) ?? throw new InvalidOperationException("La geografía no existe.");
                geografia.Region = Input.Region;
                geografia.RegionCorto = Input.RegionCorto;
                geografia.GrupoRegion = Input.GrupoRegion;
                geografia.CodigoPais = Input.CodigoPais;
                geografia.Pais = Input.Pais;
                geografia.CodigoPostal = Input.CodigoPostal;
                MensajeExito = "Geografía actualizada correctamente.";
            }
            else
            {
                if (await _db.Geografias.AnyAsync(g => g.IDGeografia == Input.IDGeografia))
                {
                    throw new InvalidOperationException($"Ya existe una geografía con el ID {Input.IDGeografia}.");
                }

                _db.Geografias.Add(new Models.Geografia
                {
                    IDGeografia = Input.IDGeografia,
                    Region = Input.Region,
                    RegionCorto = Input.RegionCorto,
                    GrupoRegion = Input.GrupoRegion,
                    CodigoPais = Input.CodigoPais,
                    Pais = Input.Pais,
                    CodigoPostal = Input.CodigoPostal,
                });
                MensajeExito = "Geografía creada correctamente.";
            }

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ObtenerMensajeError(ex));
            await CargarPermisosAsync();
            EditandoId = editando;
            await CargarListaAsync();
            return Page();
        }

        return RedirectToPage(new { filtroTexto = FiltroTexto });
    }

    public async Task<IActionResult> OnPostEliminarAsync(int id)
    {
        if (!await PuedeEliminarAsync())
        {
            return Forbid();
        }

        try
        {
            var geografia = await _db.Geografias.FindAsync(id) ?? throw new InvalidOperationException("La geografía no existe.");
            _db.Geografias.Remove(geografia);
            await _db.SaveChangesAsync();
            MensajeExito = "Geografía eliminada.";
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
        }

        return RedirectToPage(new { filtroTexto = FiltroTexto });
    }

    private async Task CargarListaAsync()
    {
        var query = _db.Geografias.AsQueryable();
        if (!string.IsNullOrWhiteSpace(FiltroTexto))
        {
            var texto = FiltroTexto.Trim();
            query = query.Where(g => EF.Functions.Like(g.Region, $"%{texto}%")
                || EF.Functions.Like(g.RegionCorto, $"%{texto}%")
                || EF.Functions.Like(g.Pais, $"%{texto}%"));
        }

        Geografias = await query.OrderBy(g => g.Region).ToListAsync();
    }

    private async Task CargarPermisosAsync()
    {
        PuedeVer = await PuedeVerAsync();
        PuedeCrear = await PuedeCrearAsync();
        PuedeEditar = await PuedeEditarAsync();
        PuedeEliminar = await PuedeEliminarAsync();
    }
}
