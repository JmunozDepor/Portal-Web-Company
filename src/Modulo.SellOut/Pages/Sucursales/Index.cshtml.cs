using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Modulo.SellOut.Data;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.SellOut.Pages.Sucursales;

/// <summary>
/// Mantenedor de dbo.Sucursal -- clave compuesta (IdCliente, IdSucursal), FK real a
/// Cliente + FK logicas a Localizacion/GrupoRetail. Puede tener muchos miles de filas
/// entre todos los clientes retail, asi que pagina server-side igual que ClienteSku
/// (Skip/Take, tamanos reusados de DocumentListViewModel.AvailablePageSizes).
/// </summary>
public class IndexModel : PageModelBaseSellOut
{
    private readonly SellOutDbContext _db;

    public IndexModel(SellOutDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    protected override string CodigoMenu => "sucursales";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? FiltroIdCliente { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FiltroTexto { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int TamanoPagina { get; set; } = 50;

    public IReadOnlyList<SucursalFila> Sucursales { get; set; } = Array.Empty<SucursalFila>();
    public List<SelectListItem> ClientesDisponibles { get; set; } = new();
    public List<SelectListItem> LocalizacionesDisponibles { get; set; } = new();
    public List<SelectListItem> GruposRetailDisponibles { get; set; } = new();
    public (int IdCliente, int IdSucursal)? Editando { get; set; }
    public bool PuedeVer { get; set; }
    public bool PuedeCrear { get; set; }
    public bool PuedeEditar { get; set; }
    public bool PuedeEliminar { get; set; }
    public int TotalRegistros { get; set; }
    public int TotalPaginas => TotalRegistros == 0 ? 1 : (int)Math.Ceiling(TotalRegistros / (double)TamanoPagina);
    public IReadOnlyList<int> AvailablePageSizes => DocumentListViewModel.AvailablePageSizes;

    public record SucursalFila(int IdCliente, int IdSucursal, string? NomCliente, string? NomSucursal, string? NomSucursalCliente,
        bool? Activo, string? Canal, string? Cadena);

    public class InputModel
    {
        [Required]
        public int IdCliente { get; set; }

        [Required]
        public int IdSucursal { get; set; }

        public string? NomSucursalCliente { get; set; }
        public string? NomSucursal { get; set; }
        public bool Activo { get; set; } = true;
        public string? Canal { get; set; }
        public string? SubCanal { get; set; }
        public string? Cadena { get; set; }
        public string? SubCadena { get; set; }
        public int? IDLocalizacion { get; set; }
        public int? IDGrupoRetail { get; set; }
        public string? SubCliente { get; set; }
        public string? GrupoSell { get; set; }
        public string? Supervisor { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int? editandoCliente, int? editandoSucursal)
    {
        await CargarPermisosAsync();
        if (!PuedeVer)
        {
            return Forbid();
        }

        if (editandoCliente is { } ec && editandoSucursal is { } es)
        {
            Editando = (ec, es);
        }

        await CargarListasAsync();

        if (Editando is { } clave)
        {
            var sucursal = await _db.Sucursales.FindAsync(clave.IdCliente, clave.IdSucursal);
            if (sucursal is not null)
            {
                Input = new InputModel
                {
                    IdCliente = sucursal.IdCliente,
                    IdSucursal = sucursal.IdSucursal,
                    NomSucursalCliente = sucursal.NomSucursalCliente,
                    NomSucursal = sucursal.NomSucursal,
                    Activo = sucursal.Activo ?? true,
                    Canal = sucursal.Canal,
                    SubCanal = sucursal.SubCanal,
                    Cadena = sucursal.Cadena,
                    SubCadena = sucursal.SubCadena,
                    IDLocalizacion = sucursal.IDLocalizacion,
                    IDGrupoRetail = sucursal.IDGrupoRetail,
                    SubCliente = sucursal.SubCliente,
                    GrupoSell = sucursal.GrupoSell,
                    Supervisor = sucursal.Supervisor,
                };
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostGuardarAsync()
    {
        var editando = Editando is not null;
        if (!await (editando ? PuedeEditarAsync() : PuedeCrearAsync()))
        {
            return Forbid();
        }

        // El [BindProperty] de Editando no viaja en el POST (no es un campo del form) --
        // se reconstruye desde el propio Input, que si trae IdCliente/IdSucursal ocultos.
        Editando = editando ? (Input.IdCliente, Input.IdSucursal) : null;

        if (!ModelState.IsValid)
        {
            await CargarPermisosAsync();
            await CargarListasAsync();
            return Page();
        }

        try
        {
            if (editando)
            {
                var sucursal = await _db.Sucursales.FindAsync(Input.IdCliente, Input.IdSucursal)
                    ?? throw new InvalidOperationException("La sucursal no existe.");
                AplicarInput(sucursal);
                sucursal.UpdateDate = DateTime.Now;
                MensajeExito = "Sucursal actualizada correctamente.";
            }
            else
            {
                if (await _db.Sucursales.AnyAsync(s => s.IdCliente == Input.IdCliente && s.IdSucursal == Input.IdSucursal))
                {
                    throw new InvalidOperationException($"Ya existe la sucursal {Input.IdSucursal} para este cliente.");
                }

                var sucursal = new Models.Sucursal { IdCliente = Input.IdCliente, IdSucursal = Input.IdSucursal, UpdateDate = DateTime.Now };
                AplicarInput(sucursal);
                _db.Sucursales.Add(sucursal);
                MensajeExito = "Sucursal creada correctamente.";
            }

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ObtenerMensajeError(ex));
            await CargarPermisosAsync();
            await CargarListasAsync();
            return Page();
        }

        return RedirectToPage(new { filtroIdCliente = FiltroIdCliente, filtroTexto = FiltroTexto, pagina = Pagina, tamanoPagina = TamanoPagina });
    }

    public async Task<IActionResult> OnPostEliminarAsync(int idCliente, int idSucursal)
    {
        if (!await PuedeEliminarAsync())
        {
            return Forbid();
        }

        try
        {
            var sucursal = await _db.Sucursales.FindAsync(idCliente, idSucursal) ?? throw new InvalidOperationException("La sucursal no existe.");
            _db.Sucursales.Remove(sucursal);
            await _db.SaveChangesAsync();
            MensajeExito = "Sucursal eliminada.";
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
        }

        return RedirectToPage(new { filtroIdCliente = FiltroIdCliente, filtroTexto = FiltroTexto, pagina = Pagina, tamanoPagina = TamanoPagina });
    }

    private void AplicarInput(Models.Sucursal sucursal)
    {
        sucursal.NomSucursalCliente = Input.NomSucursalCliente;
        sucursal.NomSucursal = Input.NomSucursal;
        sucursal.Activo = Input.Activo;
        sucursal.Canal = Input.Canal;
        sucursal.SubCanal = Input.SubCanal;
        sucursal.Cadena = Input.Cadena;
        sucursal.SubCadena = Input.SubCadena;
        sucursal.IDLocalizacion = Input.IDLocalizacion;
        sucursal.IDGrupoRetail = Input.IDGrupoRetail;
        sucursal.SubCliente = Input.SubCliente;
        sucursal.GrupoSell = Input.GrupoSell;
        sucursal.Supervisor = Input.Supervisor;
    }

    private async Task CargarListasAsync()
    {
        var clientes = await _db.Clientes.OrderBy(c => c.NomCliente).ToListAsync();
        ClientesDisponibles = clientes.Select(c => new SelectListItem(c.NomCliente, c.IdCliente.ToString())).ToList();
        var nombrePorCliente = clientes.ToDictionary(c => c.IdCliente, c => c.NomCliente);

        var localizaciones = await _db.Localizaciones.OrderBy(l => l.Ciudad).ToListAsync();
        LocalizacionesDisponibles = localizaciones
            .Select(l => new SelectListItem(l.Ciudad ?? $"Localización {l.IDLocalizacion}", l.IDLocalizacion.ToString()))
            .ToList();

        var grupos = await _db.GruposRetail.OrderBy(g => g.NombreGrupoRetail).ToListAsync();
        GruposRetailDisponibles = grupos
            .Select(g => new SelectListItem(g.NombreGrupoRetail ?? $"Grupo {g.IDGrupoRetail}", g.IDGrupoRetail.ToString()))
            .ToList();

        var query = _db.Sucursales.AsQueryable();
        if (FiltroIdCliente is { } idCliente)
        {
            query = query.Where(s => s.IdCliente == idCliente);
        }
        if (!string.IsNullOrWhiteSpace(FiltroTexto))
        {
            var texto = FiltroTexto.Trim();
            query = query.Where(s => (s.NomSucursal != null && EF.Functions.Like(s.NomSucursal, $"%{texto}%"))
                || (s.NomSucursalCliente != null && EF.Functions.Like(s.NomSucursalCliente, $"%{texto}%"))
                || (s.Canal != null && EF.Functions.Like(s.Canal, $"%{texto}%"))
                || (s.Cadena != null && EF.Functions.Like(s.Cadena, $"%{texto}%")));
        }

        TotalRegistros = await query.CountAsync();

        var tamano = AvailablePageSizes.Contains(TamanoPagina) ? TamanoPagina : 50;
        var pagina = Math.Max(1, Pagina);

        var sucursales = await query.OrderBy(s => s.IdCliente).ThenBy(s => s.IdSucursal)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync();
        Sucursales = sucursales
            .Select(s => new SucursalFila(s.IdCliente, s.IdSucursal, nombrePorCliente.TryGetValue(s.IdCliente, out var nombre) ? nombre : null,
                s.NomSucursal, s.NomSucursalCliente, s.Activo, s.Canal, s.Cadena))
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
