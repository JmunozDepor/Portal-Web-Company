using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos.Eerr;

public class IndexModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;

    // Orden y etiqueta fija de los grupos contables (CodigoGrupo en Staging solo es confiable para 6-9;
    // para 4-5 las filas RESUMEN traen CodigoGrupo='RESUMEN' sin distinguir, así que el grupo real se
    // deriva del primer dígito de NroCuenta en los dos casos, para no depender de esa inconsistencia).
    private static readonly (string Codigo, string Nombre)[] GruposOrden =
    {
        ("4", "4 - INGRESOS"),
        ("5", "5 - COSTOS DE VENTAS"),
        ("6", "6 - GASTOS OPERACIONALES"),
        ("7", "7 - OTROS INGRESOS Y EGRESOS"),
        ("8", "8 - OTROS GASTOS"),
        ("9", "9 - IMPUESTOS"),
    };

    // Orden preferido de canales en las columnas (mismo orden en que se cargó Maestro_Sucursal).
    private static readonly string[] OrdenCanales = { "TPR", "ECM", "MAY", "REG", "OTROS" };

    private record LineaEerr(string Grupo, string? CodCanal, string? Canal, string? CodSucursal, string? Sucursal, string? CodCentroCosto, bool EsGastoIndirecto, decimal Monto);

    public IndexModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    public EerrViewModel Datos { get; set; } = new();

    public async Task OnGetAsync(int? anio, string? canal, string? sucursal, string? centroCosto)
    {
        var aniosDisponibles = (await _db.CierreMes.Select(c => c.AnioMes.Substring(0, 4)).Distinct().ToListAsync())
            .Select(int.Parse)
            .OrderByDescending(a => a)
            .ToList();

        var maestro = await _db.MaestroSucursal.Where(m => m.Activo).ToListAsync();

        var vm = new EerrViewModel
        {
            AniosDisponibles = aniosDisponibles,
            Canal = canal,
            Sucursal = sucursal,
            CentroCosto = centroCosto,
            Canales = maestro.Select(m => (m.CodCanal, m.Canal)).Distinct().OrderBy(c => c.Item1).ToList(),
            Sucursales = maestro.Select(m => (m.CodSucursal, m.Sucursal)).Distinct().OrderBy(c => c.Item1).ToList(),
            CentrosCosto = maestro.Select(m => (m.CodCentroCosto, m.CentroCosto)).Distinct().OrderBy(c => c.Item1).ToList(),
        };

        if (aniosDisponibles.Count == 0)
        {
            Datos = vm;
            return;
        }

        // Por defecto, el año del período de trabajo actual (cookie compartida con el resto de la app);
        // si no hay cookie o no calza con ningún año cargado, el más reciente disponible.
        if (anio == null)
        {
            var periodo = Request.Cookies["GDG_PeriodoTrabajo"];
            anio = (periodo != null && periodo.Length >= 4 && int.TryParse(periodo[..4], out var anioCookie) && aniosDisponibles.Contains(anioCookie))
                ? anioCookie
                : aniosDisponibles.First();
        }
        vm.Anio = anio.Value;
        string prefijoAnio = vm.Anio.ToString();

        // Grupos 6-9: Distribucion_Final, con el destino ya corregido (auto o manual) — "la información modificada".
        var lineas69 = await _db.DistribucionFinal
            .Where(d => d.AnioMes.StartsWith(prefijoAnio) && d.NroCuenta != null)
            .Select(d => new LineaEerr(d.NroCuenta!.Substring(0, 1), d.CodCanalDestino, d.CanalDestino, d.CodSucursalDestino, d.SucursalDestino, d.CodCentroCostoDestino, d.EsGastoIndirecto, d.MontoDistribuido))
            .ToListAsync();

        // Grupos 4-5: Staging RESUMEN, sin modificar (ventas/costo de venta no se tocan en este sistema).
        // El monto viene naturalmente negativo en cuentas de ingreso (crédito > débito) — mismo ajuste que ya usa vw_VentaBaseDistribucion.
        // No participan de la marca directo/indirecto (eso solo aplica a gastos, grupos 6-9).
        var lineas45 = await _db.StagingCentralizacion
            .Where(s => s.AnioMes.StartsWith(prefijoAnio) && s.TipoRegistro == "RESUMEN" && s.NroCuenta != null)
            .Select(s => new LineaEerr(s.NroCuenta!.Substring(0, 1), s.CodCanal, s.Canal, s.CodSucursal, s.Sucursal, s.CodCentroCosto, false, s.MontoNeto))
            .ToListAsync();

        // Se guarda el monto con su signo contable real (Débito - Crédito) en los 6 grupos, sin forzar
        // valor absoluto acá: Ingresos (grupo 4, cuenta de crédito) da negativo y Costo/Gastos/Otros/Impuestos
        // (grupos 5-9, cuentas de débito) dan positivo cuando hay más débito que crédito. Con esto el
        // Estado de Resultados se calcula abajo con la resta real (Ingresos - Costo - Gastos - ...),
        // y recién al armar la grilla de pantalla se les aplica ABS() a las filas 4 y 5 para mostrarlas
        // como magnitud positiva (igual que el pivot de Excel de referencia).
        IEnumerable<LineaEerr> combinadas = lineas69.Concat(lineas45);

        if (!string.IsNullOrEmpty(canal))
            combinadas = combinadas.Where(l => l.CodCanal == canal);
        if (!string.IsNullOrEmpty(sucursal))
            combinadas = combinadas.Where(l => l.CodSucursal == sucursal);
        if (!string.IsNullOrEmpty(centroCosto))
            combinadas = combinadas.Where(l => l.CodCentroCosto == centroCosto);

        // Si se filtra un canal puntual, las columnas pasan a ser sus sucursales (mismo "drill down" que el pivot de Excel).
        bool drillSucursal = !string.IsNullOrEmpty(canal);
        vm.ColumnasSonSucursal = drillSucursal;

        // El nombre de columna sale de la propia línea (Canal/Sucursal ya guardados, corregidos o no), no
        // del maestro — así una línea todavía sin corregir (ej. canal "IND3" original de SAP, que no está
        // en Maestro_Sucursal) muestra su nombre real en vez de un código pelado o "(sin canal)" indebido.
        string EtiquetaColumna(LineaEerr l) => drillSucursal
            ? (string.IsNullOrEmpty(l.CodSucursal) ? "(sin sucursal)" : (string.IsNullOrWhiteSpace(l.Sucursal) ? l.CodSucursal : l.Sucursal))
            : (string.IsNullOrEmpty(l.CodCanal) ? "(sin canal)" : (string.IsNullOrWhiteSpace(l.Canal) ? l.CodCanal : l.Canal));

        // Para poder ordenar las columnas en el orden preferido, se guarda a qué código corresponde cada etiqueta.
        var codigoDeEtiqueta = new Dictionary<string, string>();

        var celdas = GruposOrden.ToDictionary(g => g.Nombre, g => new Dictionary<string, decimal>());
        var totalesColumna = new Dictionary<string, decimal>();
        decimal totalGeneral = 0;
        decimal gastosDirectos = 0;   // grupos 6-9, EsGastoIndirecto = false (quedan en Resultado Operacional 1)
        decimal gastosIndirectos = 0; // grupos 6-9, EsGastoIndirecto = true (recién se restan en Resultado Operacional 2)

        foreach (var l in combinadas)
        {
            var grupo = GruposOrden.FirstOrDefault(g => g.Codigo == l.Grupo);
            if (grupo.Nombre == null)
                continue; // cuenta fuera de los grupos 4-9 (no debería pasar, pero no se descarta silenciosamente el total)

            var columna = EtiquetaColumna(l);
            codigoDeEtiqueta[columna] = (drillSucursal ? l.CodSucursal : l.CodCanal) ?? "";
            var filaGrupo = celdas[grupo.Nombre];
            filaGrupo[columna] = filaGrupo.GetValueOrDefault(columna) + l.Monto;
            totalesColumna[columna] = totalesColumna.GetValueOrDefault(columna) + l.Monto;
            totalGeneral += l.Monto;

            if (l.Grupo is "6" or "7" or "8" or "9")
            {
                if (l.EsGastoIndirecto) gastosIndirectos += l.Monto;
                else gastosDirectos += l.Monto;
            }
        }

        // --- Estado de resultados (con el signo contable real, antes de convertir a valor absoluto) ---
        decimal TotalRaw(string nombreGrupo) => celdas.TryGetValue(nombreGrupo, out var fila) ? fila.Values.Sum() : 0;

        vm.Ingresos = -TotalRaw("4 - INGRESOS"); // se invierte: cuenta de crédito, el saldo natural da negativo
        vm.CostoVentas = TotalRaw("5 - COSTOS DE VENTAS");
        vm.UtilidadBruta = vm.Ingresos - vm.CostoVentas;
        vm.GastosDirectos = gastosDirectos;
        vm.ResultadoOperacional1 = vm.UtilidadBruta - vm.GastosDirectos;
        vm.GastosIndirectos = gastosIndirectos;
        vm.ResultadoOperacional2 = vm.ResultadoOperacional1 - vm.GastosIndirectos;
        vm.ResultadoFinal = vm.ResultadoOperacional2; // no queda nada más que restar después de 6-9

        // Resultado final por columna (mismo cálculo con signo real que el cuadro de arriba, pero abierto
        // por canal/sucursal, sin distinguir directo/indirecto) — esto es lo que se muestra en la fila de
        // abajo de la grilla en vez de una suma plana de las 6 filas (que no significa nada: mezclaría
        // ingresos con gastos sin restar).
        decimal RawCell(string grupo, string columna) => celdas.TryGetValue(grupo, out var f) && f.TryGetValue(columna, out var v) ? v : 0;
        var todasLasColumnas = celdas.Values.SelectMany(f => f.Keys).Distinct().ToList();
        var resultadoPorColumna = todasLasColumnas.ToDictionary(
            col => col,
            col => -RawCell("4 - INGRESOS", col) - RawCell("5 - COSTOS DE VENTAS", col) - RawCell("6 - GASTOS OPERACIONALES", col)
                   - RawCell("7 - OTROS INGRESOS Y EGRESOS", col) - RawCell("8 - OTROS GASTOS", col) - RawCell("9 - IMPUESTOS", col));

        // --- Grilla de pantalla: grupos 4 y 5 se muestran con el mismo signo que Ingresos/CostoVentas
        // de arriba (grupo 4 se invierte -- crédito, signo natural negativo -- grupo 5 se deja tal
        // cual -- débito, signo natural positivo). Un único factor por grupo, NUNCA Math.Abs() celda
        // por celda: si una columna puntual rompe el signo esperado (ej. una devolución que dejó el
        // costo neto de un centro/canal en negativo ese mes), Abs() por celda la vuelve positiva y
        // termina SUMANDO el doble de esa diferencia en vez de netearla -- la fila de la grilla deja
        // de sumar lo mismo que el Estado de Resultados de arriba (bug real encontrado 2026-07-20:
        // columna "Central" de Costo de Ventas, con signo real negativo, inflaba el total de la
        // grilla en 2x su valor respecto a vm.CostoVentas).
        var factorPorGrupo = new Dictionary<string, decimal> { ["4 - INGRESOS"] = -1, ["5 - COSTOS DE VENTAS"] = 1 };
        foreach (var (nombreGrupo, factor) in factorPorGrupo)
        {
            if (!celdas.TryGetValue(nombreGrupo, out var fila))
                continue;
            foreach (var columna in fila.Keys.ToList())
                fila[columna] *= factor;
        }

        vm.Grupos = GruposOrden.Select(g => g.Nombre).Where(n => celdas[n].Count > 0).ToList();
        vm.Celdas = celdas;
        vm.TotalesColumna = resultadoPorColumna;
        vm.TotalGeneral = vm.ResultadoFinal;
        vm.TotalesFila = vm.Grupos.ToDictionary(g => g, g => celdas[g].Values.Sum());

        vm.Columnas = drillSucursal
            ? totalesColumna.Keys.OrderBy(c => c).ToList()
            : totalesColumna.Keys
                .OrderBy(c => { var i = Array.IndexOf(OrdenCanales, codigoDeEtiqueta.GetValueOrDefault(c, "")); return i < 0 ? int.MaxValue : i; })
                .ThenBy(c => c)
                .ToList();

        Datos = vm;
    }
}
