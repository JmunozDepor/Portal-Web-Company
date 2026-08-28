using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Modulo.SellOut.Data;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.SellOut.Pages.ClienteSku;

/// <summary>
/// Mantenedor de dbo.ClienteSku -- clave compuesta (IdCliente, Sku). A diferencia de la
/// paridad de SAP (IParidadCatalogoService, acotada al catalogo vigente), esta tabla
/// arrastra TODOS los productos historicos de cada cliente retail -- puede tener decenas
/// de miles de filas, de ahi la paginacion server-side (mismo patron LIMIT/OFFSET que
/// GenericoVentaService.ListarAsync) en vez de traer todo de una.
///
/// "Producto" es la paridad (codigo SAP) de ese SKU del cliente. El sentinela
/// "__SIN_PARIDAD__" marca un SKU todavia no emparejado -- FiltroSoloSinParidad filtra
/// por el, y OnPostImportarParidadAsync permite resolverlos en lote subiendo un CSV
/// (columnas IdCliente, Sku, Producto) en vez de editar fila por fila.
/// </summary>
public class IndexModel : PageModelBaseSellOut
{
    private const string SentinelSinParidad = "__SIN_PARIDAD__";

    private readonly SellOutDbContext _db;

    public IndexModel(SellOutDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    protected override string CodigoMenu => "clientesku";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? FiltroIdCliente { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FiltroTexto { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool FiltroSoloSinParidad { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Pagina { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int TamanoPagina { get; set; } = 50;

    public IReadOnlyList<ClienteSkuFila> Skus { get; set; } = Array.Empty<ClienteSkuFila>();
    public List<SelectListItem> ClientesDisponibles { get; set; } = new();
    public (int IdCliente, string Sku)? Editando { get; set; }
    public bool PuedeVer { get; set; }
    public bool PuedeCrear { get; set; }
    public bool PuedeEditar { get; set; }
    public bool PuedeEliminar { get; set; }
    public int TotalRegistros { get; set; }
    public int TotalPaginas => TotalRegistros == 0 ? 1 : (int)Math.Ceiling(TotalRegistros / (double)TamanoPagina);
    public IReadOnlyList<int> AvailablePageSizes => DocumentListViewModel.AvailablePageSizes;

    public int? ActualizadosImportacion { get; set; }
    public IReadOnlyList<string>? ErroresImportacion { get; set; }

    public record ClienteSkuFila(int IdCliente, string? NomCliente, string Sku, string? DescripcionCliente, string? Barcode,
        decimal? CostoUnit, decimal? VentaUnit, string? Producto, bool? Activo);

    public class InputModel
    {
        [Required]
        public int IdCliente { get; set; }

        [Required]
        public string Sku { get; set; } = string.Empty;

        public string? Barcode { get; set; }
        public string? DescripcionCliente { get; set; }
        public string? DeptoCliente { get; set; }
        public decimal? CostoUnit { get; set; }
        public decimal? VentaUnit { get; set; }
        public decimal? PrecioFull { get; set; }
        public string? Producto { get; set; }
        public bool Activo { get; set; } = true;
    }

    public async Task<IActionResult> OnGetAsync(int? editandoCliente, string? editandoSku)
    {
        await CargarPermisosAsync();
        if (!PuedeVer)
        {
            return Forbid();
        }

        if (editandoCliente is { } ec && !string.IsNullOrEmpty(editandoSku))
        {
            Editando = (ec, editandoSku);
        }

        await CargarListasAsync();

        if (Editando is { } clave)
        {
            var sku = await _db.ClienteSkus.FindAsync(clave.IdCliente, clave.Sku);
            if (sku is not null)
            {
                Input = new InputModel
                {
                    IdCliente = sku.IdCliente,
                    Sku = sku.Sku,
                    Barcode = sku.Barcode,
                    DescripcionCliente = sku.DescripcionCliente,
                    DeptoCliente = sku.DeptoCliente,
                    CostoUnit = sku.CostoUnit,
                    VentaUnit = sku.VentaUnit,
                    PrecioFull = sku.PrecioFull,
                    Producto = sku.Producto,
                    Activo = sku.Activo ?? true,
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

        Editando = editando ? (Input.IdCliente, Input.Sku) : null;

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
                var sku = await _db.ClienteSkus.FindAsync(Input.IdCliente, Input.Sku) ?? throw new InvalidOperationException("El SKU no existe.");
                AplicarInput(sku);
                sku.UpdateDate = DateTime.Now;
                MensajeExito = "SKU actualizado correctamente.";
            }
            else
            {
                if (await _db.ClienteSkus.AnyAsync(s => s.IdCliente == Input.IdCliente && s.Sku == Input.Sku))
                {
                    throw new InvalidOperationException("Ya existe ese SKU para este cliente.");
                }

                var sku = new Models.ClienteSku { IdCliente = Input.IdCliente, Sku = Input.Sku, UpdateDate = DateTime.Now };
                AplicarInput(sku);
                _db.ClienteSkus.Add(sku);
                MensajeExito = "SKU creado correctamente.";
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

        return RedirectToPage(new { filtroIdCliente = FiltroIdCliente, filtroTexto = FiltroTexto, filtroSoloSinParidad = FiltroSoloSinParidad, pagina = Pagina, tamanoPagina = TamanoPagina });
    }

    public async Task<IActionResult> OnPostEliminarAsync(int idCliente, string sku)
    {
        if (!await PuedeEliminarAsync())
        {
            return Forbid();
        }

        try
        {
            var entidad = await _db.ClienteSkus.FindAsync(idCliente, sku) ?? throw new InvalidOperationException("El SKU no existe.");
            _db.ClienteSkus.Remove(entidad);
            await _db.SaveChangesAsync();
            MensajeExito = "SKU eliminado.";
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
        }

        return RedirectToPage(new { filtroIdCliente = FiltroIdCliente, filtroTexto = FiltroTexto, filtroSoloSinParidad = FiltroSoloSinParidad, pagina = Pagina, tamanoPagina = TamanoPagina });
    }

    /// <summary>
    /// Actualizacion masiva de la paridad (Producto) via CSV -- columnas IdCliente, Sku,
    /// Producto (encabezado, en cualquier orden). Cada fila resuelve un SKU ya existente
    /// (no crea SKU nuevos, solo actualiza la paridad de los que ya estan cargados) --
    /// mismo criterio de bitacora de errores por fila que los importadores CSV de
    /// Modulo.Ventas/Compras/Inventario, sin persistir el archivo (se procesa y se
    /// descarta en el mismo request).
    /// </summary>
    public async Task<IActionResult> OnPostImportarParidadAsync(IFormFile? archivoParidad)
    {
        if (!await PuedeEditarAsync())
        {
            return Forbid();
        }

        await CargarPermisosAsync();

        if (archivoParidad is null || archivoParidad.Length == 0)
        {
            MensajeError = "Seleccioná un archivo CSV con columnas IdCliente, Sku y Producto.";
            await CargarListasAsync();
            return Page();
        }

        var errores = new List<string>();
        var actualizados = 0;

        using (var lector = new StreamReader(archivoParidad.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            var encabezado = await lector.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(encabezado))
            {
                MensajeError = "El archivo está vacío.";
                await CargarListasAsync();
                return Page();
            }

            var delimitador = encabezado.Contains(';') ? ';' : ',';
            var columnas = encabezado.Split(delimitador).Select(c => c.Trim().Trim('"')).ToList();
            var idxIdCliente = columnas.FindIndex(c => string.Equals(c, "IdCliente", StringComparison.OrdinalIgnoreCase));
            var idxSku = columnas.FindIndex(c => string.Equals(c, "Sku", StringComparison.OrdinalIgnoreCase));
            var idxProducto = columnas.FindIndex(c => string.Equals(c, "Producto", StringComparison.OrdinalIgnoreCase));

            if (idxIdCliente < 0 || idxSku < 0 || idxProducto < 0)
            {
                MensajeError = "El archivo debe tener las columnas IdCliente, Sku y Producto en el encabezado.";
                await CargarListasAsync();
                return Page();
            }

            var numeroFila = 1;
            string? linea;
            while ((linea = await lector.ReadLineAsync()) is not null)
            {
                numeroFila++;
                if (string.IsNullOrWhiteSpace(linea))
                {
                    continue;
                }

                var valores = linea.Split(delimitador).Select(v => v.Trim().Trim('"')).ToArray();
                var indiceMaximo = Math.Max(idxIdCliente, Math.Max(idxSku, idxProducto));
                if (valores.Length <= indiceMaximo)
                {
                    errores.Add($"Fila {numeroFila}: columnas insuficientes.");
                    continue;
                }

                if (!int.TryParse(valores[idxIdCliente], out var idCliente))
                {
                    errores.Add($"Fila {numeroFila}: IdCliente \"{valores[idxIdCliente]}\" no es un número.");
                    continue;
                }

                var sku = valores[idxSku];
                if (string.IsNullOrWhiteSpace(sku))
                {
                    errores.Add($"Fila {numeroFila}: SKU vacío.");
                    continue;
                }

                var entidad = await _db.ClienteSkus.FindAsync(idCliente, sku);
                if (entidad is null)
                {
                    errores.Add($"Fila {numeroFila}: no existe el SKU \"{sku}\" para el cliente {idCliente}.");
                    continue;
                }

                entidad.Producto = valores[idxProducto];
                entidad.UpdateDate = DateTime.Now;
                actualizados++;
            }
        }

        await _db.SaveChangesAsync();

        ActualizadosImportacion = actualizados;
        ErroresImportacion = errores;
        MensajeExito = errores.Count == 0
            ? $"{actualizados} SKU actualizados correctamente."
            : $"{actualizados} SKU actualizados. {errores.Count} fila(s) con error (detalle abajo).";

        await CargarListasAsync();
        return Page();
    }

    private void AplicarInput(Models.ClienteSku sku)
    {
        sku.Barcode = Input.Barcode;
        sku.DescripcionCliente = Input.DescripcionCliente;
        sku.DeptoCliente = Input.DeptoCliente;
        sku.CostoUnit = Input.CostoUnit;
        sku.VentaUnit = Input.VentaUnit;
        sku.PrecioFull = Input.PrecioFull;
        sku.Producto = Input.Producto;
        sku.Activo = Input.Activo;
    }

    private async Task CargarListasAsync()
    {
        var clientes = await _db.Clientes.OrderBy(c => c.NomCliente).ToListAsync();
        ClientesDisponibles = clientes.Select(c => new SelectListItem(c.NomCliente, c.IdCliente.ToString())).ToList();
        var nombrePorCliente = clientes.ToDictionary(c => c.IdCliente, c => c.NomCliente);

        var query = _db.ClienteSkus.AsQueryable();
        if (FiltroIdCliente is { } idCliente)
        {
            query = query.Where(s => s.IdCliente == idCliente);
        }
        if (FiltroSoloSinParidad)
        {
            query = query.Where(s => s.Producto == SentinelSinParidad);
        }
        if (!string.IsNullOrWhiteSpace(FiltroTexto))
        {
            var texto = FiltroTexto.Trim();
            query = query.Where(s => EF.Functions.Like(s.Sku, $"%{texto}%")
                || (s.DescripcionCliente != null && EF.Functions.Like(s.DescripcionCliente, $"%{texto}%"))
                || (s.Barcode != null && EF.Functions.Like(s.Barcode, $"%{texto}%")));
        }

        TotalRegistros = await query.CountAsync();

        var tamano = AvailablePageSizes.Contains(TamanoPagina) ? TamanoPagina : 50;
        var pagina = Math.Max(1, Pagina);

        var skus = await query.OrderBy(s => s.IdCliente).ThenBy(s => s.Sku)
            .Skip((pagina - 1) * tamano)
            .Take(tamano)
            .ToListAsync();

        Skus = skus
            .Select(s => new ClienteSkuFila(s.IdCliente, nombrePorCliente.TryGetValue(s.IdCliente, out var nombre) ? nombre : null,
                s.Sku, s.DescripcionCliente, s.Barcode, s.CostoUnit, s.VentaUnit, s.Producto, s.Activo))
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
