using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.SellOut.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.SellOut.Pages.Clientes;

/// <summary>Mantenedor de dbo.Cliente -- raiz de la cadena Cliente -> Sucursal/Departamento/ClienteSku/CfgClienteLayoutInput.</summary>
public class IndexModel : PageModelBaseSellOut
{
    private readonly SellOutDbContext _db;

    public IndexModel(SellOutDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    protected override string CodigoMenu => "clientes";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? FiltroTexto { get; set; }

    public IReadOnlyList<Models.Cliente> Clientes { get; set; } = Array.Empty<Models.Cliente>();
    public int? EditandoId { get; set; }
    public bool PuedeVer { get; set; }
    public bool PuedeCrear { get; set; }
    public bool PuedeEditar { get; set; }
    public bool PuedeEliminar { get; set; }

    public class InputModel
    {
        [Required]
        public int IdCliente { get; set; }

        [Required]
        public string NomCliente { get; set; } = string.Empty;

        public bool Activo { get; set; } = true;
        public string? ClienteCodigo { get; set; }
        public int? OrdenReporte { get; set; }
        public int? PrimerSemInicio { get; set; }
        public int? PrimerSemFin { get; set; }
        public int? SegundoSemInicio { get; set; }
        public int? SegundoSemFin { get; set; }
        public string? Periodo { get; set; }
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
            var cliente = await _db.Clientes.FindAsync(id);
            if (cliente is not null)
            {
                Input = new InputModel
                {
                    IdCliente = cliente.IdCliente,
                    NomCliente = cliente.NomCliente ?? string.Empty,
                    Activo = cliente.Activo ?? true,
                    ClienteCodigo = cliente.ClienteCodigo,
                    OrdenReporte = cliente.OrdenReporte,
                    PrimerSemInicio = cliente.PrimerSem_Inicio,
                    PrimerSemFin = cliente.PrimerSem_Fin,
                    SegundoSemInicio = cliente.SegundoSem_Inicio,
                    SegundoSemFin = cliente.SegundoSem_Fin,
                    Periodo = cliente.Periodo,
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
                var cliente = await _db.Clientes.FindAsync(id) ?? throw new InvalidOperationException("El cliente no existe.");
                AplicarInput(cliente);
                cliente.UpdateDate = DateTime.Now;
                MensajeExito = "Cliente actualizado correctamente.";
            }
            else
            {
                if (await _db.Clientes.AnyAsync(c => c.IdCliente == Input.IdCliente))
                {
                    throw new InvalidOperationException($"Ya existe un cliente con el ID {Input.IdCliente}.");
                }

                var cliente = new Models.Cliente { IdCliente = Input.IdCliente, UpdateDate = DateTime.Now };
                AplicarInput(cliente);
                _db.Clientes.Add(cliente);
                MensajeExito = "Cliente creado correctamente.";
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
            var cliente = await _db.Clientes.FindAsync(id) ?? throw new InvalidOperationException("El cliente no existe.");
            _db.Clientes.Remove(cliente);
            await _db.SaveChangesAsync();
            MensajeExito = "Cliente eliminado.";
        }
        catch (Exception)
        {
            MensajeError = "No se pudo eliminar: es probable que tenga sucursales, SKU o configuración de layout asociada -- eliminá esos registros primero o desactivá el cliente en su lugar.";
        }

        return RedirectToPage(new { filtroTexto = FiltroTexto });
    }

    private void AplicarInput(Models.Cliente cliente)
    {
        cliente.NomCliente = Input.NomCliente;
        cliente.Activo = Input.Activo;
        cliente.ClienteCodigo = Input.ClienteCodigo;
        cliente.OrdenReporte = Input.OrdenReporte;
        cliente.PrimerSem_Inicio = Input.PrimerSemInicio;
        cliente.PrimerSem_Fin = Input.PrimerSemFin;
        cliente.SegundoSem_Inicio = Input.SegundoSemInicio;
        cliente.SegundoSem_Fin = Input.SegundoSemFin;
        cliente.Periodo = Input.Periodo;
    }

    private async Task CargarListaAsync()
    {
        var query = _db.Clientes.AsQueryable();
        if (!string.IsNullOrWhiteSpace(FiltroTexto))
        {
            var texto = FiltroTexto.Trim();
            query = query.Where(c => (c.NomCliente != null && EF.Functions.Like(c.NomCliente, $"%{texto}%"))
                || (c.ClienteCodigo != null && EF.Functions.Like(c.ClienteCodigo, $"%{texto}%")));
        }

        Clientes = await query.OrderBy(c => c.NomCliente).ToListAsync();
    }

    private async Task CargarPermisosAsync()
    {
        PuedeVer = await PuedeVerAsync();
        PuedeCrear = await PuedeCrearAsync();
        PuedeEditar = await PuedeEditarAsync();
        PuedeEliminar = await PuedeEliminarAsync();
    }
}
