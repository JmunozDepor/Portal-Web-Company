using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Modulo.SellOut.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.SellOut.Pages.Layouts;

/// <summary>
/// Mantenedor de dbo.CfgClienteLayoutInput -- clave compuesta (IdCliente, TipoArchivo).
/// Este plugin NUNCA ejecuta el texto de los campos Map* (referencia a columna staging
/// "ColN", expresion SQL de solo lectura sobre esas columnas, o el literal "Calcular") --
/// solo lo persiste para que el pipeline SSIS externo lo interprete. Aun asi,
/// ValidarExpresionMapeo valida que cada valor tenga una de esas 3 formas exactas (no solo
/// bloquear palabras peligrosas) -- mismo criterio de "falla hacia mas estricto" que
/// CONSULTA_SQL_DISPONIBLE en Modulo.Aprobacion.
/// </summary>
public class IndexModel : PageModelBaseSellOut
{
    private static readonly string[] TiposArchivoConocidos = { "Producto", "Stock", "Venta", "VentaStockProducto" };

    /// <summary>Referencia pura a una columna de staging (ej. "Col17").</summary>
    private static readonly Regex ReferenciaColumna = new(@"^Col\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Caracteres permitidos en una expresion, UNA VEZ que los literales de texto ('...')
    /// ya se reemplazaron -- letras/digitos/guion bajo (identificadores y ColN), espacio,
    /// parentesis, operadores aritmeticos, coma y punto (decimales). Nada de ";", "--",
    /// "/*", corchetes, arrobas, etc. -- eso ya alcanza para bloquear cualquier intento de
    /// separar sentencias o comentar codigo.
    /// </summary>
    private static readonly Regex CaracteresPermitidos = new(@"^[A-Za-z0-9_ \t\r\n()+\-*/%.,]*$", RegexOptions.Compiled);

    private static readonly Regex LiteralTexto = new(@"'([^']|'')*'", RegexOptions.Compiled);
    private static readonly Regex Token = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    /// <summary>Funciones SQL de lectura permitidas dentro de una formula Map* (whitelist explicita, no "todo lo que no este bloqueado").</summary>
    private static readonly HashSet<string> FuncionesPermitidas = new(StringComparer.OrdinalIgnoreCase)
    {
        "CONCAT", "CAST", "TRY_CAST", "CONVERT", "TRY_CONVERT", "SUBSTRING", "REPLACE",
        "ISNULL", "COALESCE", "LTRIM", "RTRIM", "TRIM", "UPPER", "LOWER", "LEN", "ROUND",
        "ABS", "FLOOR", "CEILING", "NULLIF", "IIF", "STUFF", "FORMAT", "STR",
    };

    /// <summary>Palabras clave de tipos de dato usadas dentro de CAST/TRY_CAST (ej. "AS NUMERIC(18,0)").</summary>
    private static readonly HashSet<string> PalabrasClave = new(StringComparer.OrdinalIgnoreCase)
    {
        "AS", "NUMERIC", "VARCHAR", "NVARCHAR", "INT", "BIGINT", "DECIMAL", "DATE",
        "DATETIME", "FLOAT", "CHAR", "NCHAR", "MONEY", "BIT", "REAL", "SMALLINT", "TINYINT",
    };

    private readonly SellOutDbContext _db;

    public IndexModel(SellOutDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    protected override string CodigoMenu => "layouts";

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? FiltroIdCliente { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FiltroTexto { get; set; }

    public IReadOnlyList<LayoutFila> Layouts { get; set; } = Array.Empty<LayoutFila>();
    public List<SelectListItem> ClientesDisponibles { get; set; } = new();
    public IReadOnlyList<string> TiposArchivo => TiposArchivoConocidos;
    public (int IdCliente, string TipoArchivo)? Editando { get; set; }
    public bool PuedeVer { get; set; }
    public bool PuedeCrear { get; set; }
    public bool PuedeEditar { get; set; }
    public bool PuedeEliminar { get; set; }

    public record LayoutFila(int IdCliente, string? NomCliente, string TipoArchivo, bool? Activo, string FilePattern);

    public class InputModel
    {
        [Required]
        public int IdCliente { get; set; }

        [Required]
        public string TipoArchivo { get; set; } = string.Empty;

        [Required]
        public string Cliente { get; set; } = string.Empty;

        public bool Activo { get; set; } = true;

        [Required]
        public string FilePattern { get; set; } = string.Empty;

        public string? MapFecha { get; set; }
        public string? MapSku { get; set; }
        public string? MapDescripcionCliente { get; set; }
        public string? MapPrcCostoUnit { get; set; }
        public string? MapPrcVtaUnit { get; set; }
        public string? MapPrcVtaFull { get; set; }
        public string? MapBarcode { get; set; }
        public string? MapDepartamento { get; set; }
        public string? MapDefinicion1 { get; set; }
        public string? MapDefinicion2 { get; set; }
        public string? MapIdSucursal { get; set; }
        public string? MapSucursal { get; set; }
        public string? MapVtaUN { get; set; }
        public string? MapVtaNeta { get; set; }
        public string? MapVtaBruta { get; set; }
        public string? MapVtaBrutaFull { get; set; }
        public string? MapVtaCosto { get; set; }
        public string? MapStkUN { get; set; }
        public string? MapStkVtaNeta { get; set; }
        public string? MapStkVtaBruta { get; set; }
        public string? MapStkCosto { get; set; }
        public bool CalcSkuBarcode { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int? editandoCliente, string? editandoTipo)
    {
        await CargarPermisosAsync();
        if (!PuedeVer)
        {
            return Forbid();
        }

        if (editandoCliente is { } ec && !string.IsNullOrEmpty(editandoTipo))
        {
            Editando = (ec, editandoTipo);
        }

        await CargarListasAsync();

        if (Editando is { } clave)
        {
            var layout = await _db.ConfiguracionesLayout.FindAsync(clave.IdCliente, clave.TipoArchivo);
            if (layout is not null)
            {
                Input = new InputModel
                {
                    IdCliente = layout.IdCliente,
                    TipoArchivo = layout.TipoArchivo,
                    Cliente = layout.Cliente,
                    Activo = layout.Activo ?? true,
                    FilePattern = layout.FilePattern,
                    MapFecha = layout.MapFecha,
                    MapSku = layout.MapSku,
                    MapDescripcionCliente = layout.MapDescripcionCliente,
                    MapPrcCostoUnit = layout.MapPrcCostoUnit,
                    MapPrcVtaUnit = layout.MapPrcVtaUnit,
                    MapPrcVtaFull = layout.MapPrcVtaFull,
                    MapBarcode = layout.MapBarcode,
                    MapDepartamento = layout.MapDepartamento,
                    MapDefinicion1 = layout.MapDefinicion1,
                    MapDefinicion2 = layout.MapDefinicion2,
                    MapIdSucursal = layout.MapIdSucursal,
                    MapSucursal = layout.MapSucursal,
                    MapVtaUN = layout.MapVtaUN,
                    MapVtaNeta = layout.MapVtaNeta,
                    MapVtaBruta = layout.MapVtaBruta,
                    MapVtaBrutaFull = layout.MapVtaBrutaFull,
                    MapVtaCosto = layout.MapVtaCosto,
                    MapStkUN = layout.MapStkUN,
                    MapStkVtaNeta = layout.MapStkVtaNeta,
                    MapStkVtaBruta = layout.MapStkVtaBruta,
                    MapStkCosto = layout.MapStkCosto,
                    CalcSkuBarcode = string.Equals(layout.CalcSkuBarcode, "Y", StringComparison.OrdinalIgnoreCase),
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

        Editando = editando ? (Input.IdCliente, Input.TipoArchivo) : null;

        ValidarExpresionesMapeo();

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
                var layout = await _db.ConfiguracionesLayout.FindAsync(Input.IdCliente, Input.TipoArchivo)
                    ?? throw new InvalidOperationException("La configuración no existe.");
                AplicarInput(layout);
                MensajeExito = "Configuración de layout actualizada correctamente.";
            }
            else
            {
                if (await _db.ConfiguracionesLayout.AnyAsync(c => c.IdCliente == Input.IdCliente && c.TipoArchivo == Input.TipoArchivo))
                {
                    throw new InvalidOperationException("Ya existe una configuración para este cliente y tipo de archivo.");
                }

                var layout = new Models.CfgClienteLayoutInput { IdCliente = Input.IdCliente, TipoArchivo = Input.TipoArchivo };
                AplicarInput(layout);
                _db.ConfiguracionesLayout.Add(layout);
                MensajeExito = "Configuración de layout creada correctamente.";
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

    public async Task<IActionResult> OnPostEliminarAsync(int idCliente, string tipoArchivo)
    {
        if (!await PuedeEliminarAsync())
        {
            return Forbid();
        }

        try
        {
            var layout = await _db.ConfiguracionesLayout.FindAsync(idCliente, tipoArchivo) ?? throw new InvalidOperationException("La configuración no existe.");
            _db.ConfiguracionesLayout.Remove(layout);
            await _db.SaveChangesAsync();
            MensajeExito = "Configuración de layout eliminada.";
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
        }

        return RedirectToPage(new { filtroIdCliente = FiltroIdCliente, filtroTexto = FiltroTexto });
    }

    /// <summary>
    /// Valida cada campo Map* contra una de 3 formas exactas: vacío (opcional), el
    /// literal "Calcular", o una expresión donde cada identificador es una referencia de
    /// columna ("Col17"), una función permitida (whitelist), o una palabra clave de tipo
    /// de dato (para CAST/TRY_CAST) -- cualquier otra cosa se rechaza. No es un blocklist
    /// de palabras peligrosas: es un allowlist de forma, así que cualquier cosa que no
    /// calce (incluidas palabras de escritura/DDL) queda afuera por construcción.
    /// </summary>
    private void ValidarExpresionesMapeo()
    {
        var campos = new (string Nombre, string? Valor)[]
        {
            (nameof(InputModel.MapFecha), Input.MapFecha),
            (nameof(InputModel.MapSku), Input.MapSku),
            (nameof(InputModel.MapDescripcionCliente), Input.MapDescripcionCliente),
            (nameof(InputModel.MapPrcCostoUnit), Input.MapPrcCostoUnit),
            (nameof(InputModel.MapPrcVtaUnit), Input.MapPrcVtaUnit),
            (nameof(InputModel.MapPrcVtaFull), Input.MapPrcVtaFull),
            (nameof(InputModel.MapBarcode), Input.MapBarcode),
            (nameof(InputModel.MapDepartamento), Input.MapDepartamento),
            (nameof(InputModel.MapDefinicion1), Input.MapDefinicion1),
            (nameof(InputModel.MapDefinicion2), Input.MapDefinicion2),
            (nameof(InputModel.MapIdSucursal), Input.MapIdSucursal),
            (nameof(InputModel.MapSucursal), Input.MapSucursal),
            (nameof(InputModel.MapVtaUN), Input.MapVtaUN),
            (nameof(InputModel.MapVtaNeta), Input.MapVtaNeta),
            (nameof(InputModel.MapVtaBruta), Input.MapVtaBruta),
            (nameof(InputModel.MapVtaBrutaFull), Input.MapVtaBrutaFull),
            (nameof(InputModel.MapVtaCosto), Input.MapVtaCosto),
            (nameof(InputModel.MapStkUN), Input.MapStkUN),
            (nameof(InputModel.MapStkVtaNeta), Input.MapStkVtaNeta),
            (nameof(InputModel.MapStkVtaBruta), Input.MapStkVtaBruta),
            (nameof(InputModel.MapStkCosto), Input.MapStkCosto),
        };

        foreach (var (nombre, valor) in campos)
        {
            var error = ValidarExpresionMapeo(valor);
            if (error is not null)
            {
                ModelState.AddModelError($"Input.{nombre}", error);
            }
        }
    }

    /// <summary>Devuelve null si el valor es válido, o el mensaje de error si no.</summary>
    private static string? ValidarExpresionMapeo(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var texto = valor.Trim();
        if (string.Equals(texto, "Calcular", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (ReferenciaColumna.IsMatch(texto))
        {
            return null;
        }

        // Los literales de texto ('...', con '' como comilla escapada dentro) son dato
        // libre -- se sacan antes de validar caracteres/tokens para no rechazar un
        // separador " | " o similar que el cliente use como texto literal.
        var sinLiterales = LiteralTexto.Replace(texto, "''");

        if (!CaracteresPermitidos.IsMatch(sinLiterales))
        {
            return "contiene caracteres no permitidos -- solo columnas (\"Col17\"), funciones, operadores (+ - * / %), paréntesis, comas y punto.";
        }

        if (ContarCaracter(sinLiterales, '(') != ContarCaracter(sinLiterales, ')'))
        {
            return "tiene paréntesis desbalanceados.";
        }

        foreach (Match match in Token.Matches(sinLiterales))
        {
            var token = match.Value;
            var restoDesdeToken = sinLiterales[(match.Index + token.Length)..].TrimStart();
            var esLlamadaFuncion = restoDesdeToken.StartsWith('(');

            if (esLlamadaFuncion)
            {
                if (!FuncionesPermitidas.Contains(token))
                {
                    return $"\"{token}\" no es una función permitida -- debe ser una columna (\"Col17\"), una función de la lista permitida, o \"Calcular\".";
                }
            }
            else if (!PalabrasClave.Contains(token) && !ReferenciaColumna.IsMatch(token))
            {
                return $"\"{token}\" no es válido -- debe ser una columna (\"Col17\"), una función permitida, un tipo de dato, o \"Calcular\".";
            }
        }

        return null;
    }

    private static int ContarCaracter(string texto, char caracter) => texto.Count(c => c == caracter);

    private void AplicarInput(Models.CfgClienteLayoutInput layout)
    {
        layout.Cliente = Input.Cliente;
        layout.Activo = Input.Activo;
        layout.FilePattern = Input.FilePattern;
        layout.MapFecha = Input.MapFecha;
        layout.MapSku = Input.MapSku;
        layout.MapDescripcionCliente = Input.MapDescripcionCliente;
        layout.MapPrcCostoUnit = Input.MapPrcCostoUnit;
        layout.MapPrcVtaUnit = Input.MapPrcVtaUnit;
        layout.MapPrcVtaFull = Input.MapPrcVtaFull;
        layout.MapBarcode = Input.MapBarcode;
        layout.MapDepartamento = Input.MapDepartamento;
        layout.MapDefinicion1 = Input.MapDefinicion1;
        layout.MapDefinicion2 = Input.MapDefinicion2;
        layout.MapIdSucursal = Input.MapIdSucursal;
        layout.MapSucursal = Input.MapSucursal;
        layout.MapVtaUN = Input.MapVtaUN;
        layout.MapVtaNeta = Input.MapVtaNeta;
        layout.MapVtaBruta = Input.MapVtaBruta;
        layout.MapVtaBrutaFull = Input.MapVtaBrutaFull;
        layout.MapVtaCosto = Input.MapVtaCosto;
        layout.MapStkUN = Input.MapStkUN;
        layout.MapStkVtaNeta = Input.MapStkVtaNeta;
        layout.MapStkVtaBruta = Input.MapStkVtaBruta;
        layout.MapStkCosto = Input.MapStkCosto;
        layout.CalcSkuBarcode = Input.CalcSkuBarcode ? "Y" : "N";
    }

    private async Task CargarListasAsync()
    {
        var clientes = await _db.Clientes.OrderBy(c => c.NomCliente).ToListAsync();
        ClientesDisponibles = clientes.Select(c => new SelectListItem(c.NomCliente, c.IdCliente.ToString())).ToList();
        var nombrePorCliente = clientes.ToDictionary(c => c.IdCliente, c => c.NomCliente);

        var query = _db.ConfiguracionesLayout.AsQueryable();
        if (FiltroIdCliente is { } idCliente)
        {
            query = query.Where(l => l.IdCliente == idCliente);
        }
        if (!string.IsNullOrWhiteSpace(FiltroTexto))
        {
            var texto = FiltroTexto.Trim();
            query = query.Where(l => EF.Functions.Like(l.TipoArchivo, $"%{texto}%")
                || EF.Functions.Like(l.FilePattern, $"%{texto}%")
                || EF.Functions.Like(l.Cliente, $"%{texto}%"));
        }

        var layouts = await query.OrderBy(l => l.IdCliente).ThenBy(l => l.TipoArchivo).ToListAsync();
        Layouts = layouts
            .Select(l => new LayoutFila(l.IdCliente, nombrePorCliente.TryGetValue(l.IdCliente, out var nombre) ? nombre : null,
                l.TipoArchivo, l.Activo, l.FilePattern))
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
