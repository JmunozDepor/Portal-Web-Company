using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Modulo.SellOut.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.SellOut.Pages.Departamentos;

/// <summary>Mantenedor de dbo.Departamento -- clave compuesta (IdCliente, ClienteDepartamento).</summary>
public class IndexModel : PageModelBaseSellOut
{
    private readonly SellOutDbContext _db;

    public IndexModel(SellOutDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    protected override string CodigoMenu => "departamentos";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? FiltroIdCliente { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FiltroTexto { get; set; }

    public IReadOnlyList<DepartamentoFila> Departamentos { get; set; } = Array.Empty<DepartamentoFila>();
    public List<SelectListItem> ClientesDisponibles { get; set; } = new();
    public (int IdCliente, string ClienteDepartamento)? Editando { get; set; }
    public bool PuedeVer { get; set; }
    public bool PuedeCrear { get; set; }
    public bool PuedeEditar { get; set; }
    public bool PuedeEliminar { get; set; }

    public record DepartamentoFila(int IdCliente, string? NomCliente, string ClienteDepartamento, string? DescripcionDeptoLocal);

    public class InputModel
    {
        [Required]
        public int IdCliente { get; set; }

        [Required]
        public string ClienteDepartamento { get; set; } = string.Empty;

        public string? DescripcionDeptoLocal { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int? editandoCliente, string? editandoDepartamento)
    {
        await CargarPermisosAsync();
        if (!PuedeVer)
        {
            return Forbid();
        }

        if (editandoCliente is { } ec && !string.IsNullOrEmpty(editandoDepartamento))
        {
            Editando = (ec, editandoDepartamento);
        }

        await CargarListasAsync();

        if (Editando is { } clave)
        {
            var departamento = await _db.Departamentos.FindAsync(clave.IdCliente, clave.ClienteDepartamento);
            if (departamento is not null)
            {
                Input = new InputModel
                {
                    IdCliente = departamento.IdCliente,
                    ClienteDepartamento = departamento.ClienteDepartamento,
                    DescripcionDeptoLocal = departamento.DescripcionDeptoLocal,
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

        Editando = editando ? (Input.IdCliente, Input.ClienteDepartamento) : null;

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
                var departamento = await _db.Departamentos.FindAsync(Input.IdCliente, Input.ClienteDepartamento)
                    ?? throw new InvalidOperationException("El departamento no existe.");
                departamento.DescripcionDeptoLocal = Input.DescripcionDeptoLocal;
                departamento.UpdateDate = DateTime.Now;
                MensajeExito = "Departamento actualizado correctamente.";
            }
            else
            {
                if (await _db.Departamentos.AnyAsync(d => d.IdCliente == Input.IdCliente && d.ClienteDepartamento == Input.ClienteDepartamento))
                {
                    throw new InvalidOperationException("Ya existe ese departamento para este cliente.");
                }

                _db.Departamentos.Add(new Models.Departamento
                {
                    IdCliente = Input.IdCliente,
                    ClienteDepartamento = Input.ClienteDepartamento,
                    DescripcionDeptoLocal = Input.DescripcionDeptoLocal,
                    UpdateDate = DateTime.Now,
                });
                MensajeExito = "Departamento creado correctamente.";
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

        return RedirectToPage(new { filtroIdCliente = FiltroIdCliente, filtroTexto = FiltroTexto });
    }

    public async Task<IActionResult> OnPostEliminarAsync(int idCliente, string clienteDepartamento)
    {
        if (!await PuedeEliminarAsync())
        {
            return Forbid();
        }

        try
        {
            var departamento = await _db.Departamentos.FindAsync(idCliente, clienteDepartamento) ?? throw new InvalidOperationException("El departamento no existe.");
            _db.Departamentos.Remove(departamento);
            await _db.SaveChangesAsync();
            MensajeExito = "Departamento eliminado.";
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
        }

        return RedirectToPage(new { filtroIdCliente = FiltroIdCliente, filtroTexto = FiltroTexto });
    }

    private async Task CargarListasAsync()
    {
        var clientes = await _db.Clientes.OrderBy(c => c.NomCliente).ToListAsync();
        ClientesDisponibles = clientes.Select(c => new SelectListItem(c.NomCliente, c.IdCliente.ToString())).ToList();
        var nombrePorCliente = clientes.ToDictionary(c => c.IdCliente, c => c.NomCliente);

        var query = _db.Departamentos.AsQueryable();
        if (FiltroIdCliente is { } idCliente)
        {
            query = query.Where(d => d.IdCliente == idCliente);
        }
        if (!string.IsNullOrWhiteSpace(FiltroTexto))
        {
            var texto = FiltroTexto.Trim();
            query = query.Where(d => EF.Functions.Like(d.ClienteDepartamento, $"%{texto}%")
                || (d.DescripcionDeptoLocal != null && EF.Functions.Like(d.DescripcionDeptoLocal, $"%{texto}%")));
        }

        var departamentos = await query.OrderBy(d => d.IdCliente).ThenBy(d => d.ClienteDepartamento).ToListAsync();
        Departamentos = departamentos
            .Select(d => new DepartamentoFila(d.IdCliente, nombrePorCliente.TryGetValue(d.IdCliente, out var nombre) ? nombre : null,
                d.ClienteDepartamento, d.DescripcionDeptoLocal))
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
