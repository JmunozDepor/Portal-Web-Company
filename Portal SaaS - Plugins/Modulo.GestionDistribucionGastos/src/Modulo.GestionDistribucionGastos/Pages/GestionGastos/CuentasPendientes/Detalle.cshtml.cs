using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Data;
using Modulo.GestionDistribucionGastos.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.GestionDistribucionGastos.Pages.GestionGastos.CuentasPendientes;

public class DetalleModel : PageModelBaseGestionGastos
{
    private readonly ApplicationDbContext _db;
    private const int MinutosLiberacionBloqueo = 15;

    public DetalleModel(ApplicationDbContext db, ICurrentUserContext usuarioActual) : base(usuarioActual)
    {
        _db = db;
    }

    public string AnioMes { get; set; } = "";
    public string NroCuenta { get; set; } = "";
    public bool MesCerrado { get; set; }
    public bool CuentaAprobadaBloqueada { get; set; }
    public string? UsuarioAprobador { get; set; }
    public DateTime? FechaAprobacionCuenta { get; set; }
    public List<DistribucionFinal> Lineas { get; set; } = new();
    public Dictionary<int, int> ConteoPorStaging { get; set; } = new();
    public Dictionary<int, StagingCentralizacion> Staging { get; set; } = new();
    public List<MaestroSucursal> MaestroSucursales { get; set; } = new();
    public List<(string Codigo, string Nombre)> CentrosCosto { get; set; } = new();
    public List<(string Codigo, string Nombre)> Canales { get; set; } = new();
    public List<(string Codigo, string Nombre)> Sucursales { get; set; } = new();

    // GET ?anioMes=202501&nroCuenta=61-08-001-11
    public async Task<IActionResult> OnGetAsync(string anioMes, string nroCuenta)
    {
        // Cookies.Append revienta con ArgumentNullException si el value es null (bug real
        // encontrado 2026-08-13 en el piloto: un GET a esta página sin anioMes en la
        // querystring -- ej. un link/bookmark viejo -- tiraba 500 acá mismo, antes de
        // llegar a ninguna validación de negocio).
        if (!string.IsNullOrWhiteSpace(anioMes))
        {
            Response.Cookies.Append("GDG_PeriodoTrabajo", anioMes, new CookieOptions { Expires = DateTimeOffset.UtcNow.AddDays(60), IsEssential = true });
        }

        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
        bool mesCerrado = cierre?.Estado == "CERRADO";

        var aprobada = await _db.CuentaAprobada.FirstOrDefaultAsync(a => a.AnioMes == anioMes && a.NroCuenta == nroCuenta);
        bool cuentaAprobada = aprobada != null;

        if (!mesCerrado && !cuentaAprobada)
        {
            // La edición queda disponible siempre que la cuenta no esté bloqueada por otro usuario, no esté
            // aprobada y el mes no esté cerrado. Si está aprobada no tiene sentido tomar el lock de edición
            // transitorio (Cuenta_En_Trabajo) sobre algo que de todos modos no se puede tocar.
            // Si se entra directo por URL sin haber pasado por "Tomar", se toma automáticamente acá.
            var bloqueo = await _db.CuentaEnTrabajo
                .FirstOrDefaultAsync(b => b.AnioMes == anioMes && b.NroCuenta == nroCuenta);

            bool vigente = bloqueo != null && (DateTime.Now - bloqueo.FechaBloqueo).TotalMinutes <= MinutosLiberacionBloqueo;

            if (vigente && bloqueo!.BloqueadoPor != NombreUsuarioActual)
            {
                MensajeError = $"Esta cuenta está bloqueada por {bloqueo.BloqueadoPor}.";
                return RedirectToPage("/GestionGastos/CuentasPendientes/Index", new { anioMes });
            }

            if (bloqueo == null)
            {
                _db.CuentaEnTrabajo.Add(new CuentaEnTrabajo { AnioMes = anioMes, NroCuenta = nroCuenta, BloqueadoPor = NombreUsuarioActual, FechaBloqueo = DateTime.Now });
                await _db.SaveChangesAsync();
            }
            else if (!vigente)
            {
                bloqueo.BloqueadoPor = NombreUsuarioActual;
                bloqueo.FechaBloqueo = DateTime.Now;
                await _db.SaveChangesAsync();
            }
        }

        // Se incluyen tanto las líneas pendientes como las ya asignadas manualmente en esta cuenta/mes
        // (TipoOrigen "MANUAL"), para que lo ya guardado no desaparezca de la vista y se pueda corregir.
        var lineas = await _db.DistribucionFinal
            .Where(d => d.AnioMes == anioMes
                        && d.NroCuenta == nroCuenta
                        && (d.TipoOrigen == "SIN_AJUSTE" || d.TipoOrigen == "MANUAL"))
            .OrderByDescending(d => d.FechaAsignacion)
            .ThenByDescending(d => d.MontoOriginal)
            .ThenBy(d => d.StagingId)
            .ThenBy(d => d.Id)
            .ToListAsync();

        // Reordenado en memoria por grupo (StagingId): desde que una división puede dejar
        // saldo pendiente en el origen (OnPostDividirLineaAsync), un mismo StagingId puede
        // tener filas con distinto Estado (partidas ya asignadas + el remanente PENDIENTE) --
        // Estado ya no sirve como clave de orden primaria porque separaría el remanente del
        // resto de sus partidas. Los grupos con algo pendiente siguen mostrándose primero
        // (igual que antes), pero SIEMPRE agrupados; dentro del grupo, las partidas ya
        // asignadas van antes que el remanente pendiente para que "esInicioDeGrupo" (ver
        // Detalle.cshtml) siga cayendo en una fila ya asignada, no en el remanente.
        lineas = lineas
            .GroupBy(d => d.StagingId)
            .OrderBy(g => g.Any(d => d.Estado == "PENDIENTE") ? 0 : 1)
            .ThenByDescending(g => g.Max(d => d.FechaAsignacion ?? DateTime.MinValue))
            .ThenByDescending(g => g.Max(d => d.MontoOriginal))
            .ThenBy(g => g.Key)
            .SelectMany(g => g.OrderBy(d => d.Estado == "PENDIENTE" ? 1 : 0).ThenBy(d => d.Id))
            .ToList();

        // Datos de la línea original en el staging (glosa, proveedor) para dar contexto
        var stagingIds = lineas.Select(l => l.StagingId).ToList();
        var staging = await _db.StagingCentralizacion
            .Where(s => stagingIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s);

        var maestroSucursal = await _db.MaestroSucursal.Where(m => m.Activo).OrderBy(m => m.CodSucursal).ToListAsync();

        AnioMes = anioMes;
        NroCuenta = nroCuenta;
        MesCerrado = mesCerrado;
        CuentaAprobadaBloqueada = cuentaAprobada;
        UsuarioAprobador = aprobada?.UsuarioAprobador;
        FechaAprobacionCuenta = aprobada?.FechaAprobacion;
        Lineas = lineas;
        ConteoPorStaging = lineas.GroupBy(l => l.StagingId).ToDictionary(g => g.Key, g => g.Count());
        Staging = staging;
        MaestroSucursales = maestroSucursal;
        CentrosCosto = await ObtenerDimension(anioMes, "CentroCosto", maestroSucursal);
        Canales = await ObtenerDimension(anioMes, "Canal", maestroSucursal);
        Sucursales = await ObtenerDimension(anioMes, "Sucursal", maestroSucursal);

        return Page();
    }

    private async Task<List<(string Codigo, string Nombre)>> ObtenerDimension(string anioMes, string tipo, List<MaestroSucursal> maestro)
    {
        // Se listan los valores distintos que ya existen en el staging del mes, como catálogo rápido,
        // y se completan con Maestro_Sucursal (fuente de verdad de la correlación sucursal/canal/centro de costo).
        IQueryable<StagingCentralizacion> query = _db.StagingCentralizacion.Where(s => s.AnioMes == anioMes);

        var deStaging = tipo switch
        {
            "CentroCosto" => await query.Select(s => new { s.CodCentroCosto, s.CentroCosto }).Distinct()
                .Select(x => new ValueTuple<string, string>(x.CodCentroCosto ?? "", x.CentroCosto ?? "")).ToListAsync(),
            "Canal" => await query.Select(s => new { s.CodCanal, s.Canal }).Distinct()
                .Select(x => new ValueTuple<string, string>(x.CodCanal ?? "", x.Canal ?? "")).ToListAsync(),
            "Sucursal" => await query.Select(s => new { s.CodSucursal, s.Sucursal }).Distinct()
                .Select(x => new ValueTuple<string, string>(x.CodSucursal ?? "", x.Sucursal ?? "")).ToListAsync(),
            _ => new List<(string, string)>()
        };

        var deMaestro = tipo switch
        {
            "CentroCosto" => maestro.Select(m => (m.CodCentroCosto, m.CentroCosto)).ToList(),
            "Canal" => maestro.Select(m => (m.CodCanal, m.Canal)).ToList(),
            "Sucursal" => maestro.Select(m => (m.CodSucursal, m.Sucursal)).ToList(),
            _ => new List<(string, string)>()
        };

        // Maestro_Sucursal manda: si un código aparece en ambas fuentes, se usa su nombre desde el maestro.
        return deMaestro.Concat(deStaging)
            .Where(v => !string.IsNullOrWhiteSpace(v.Item1))
            .GroupBy(v => v.Item1)
            .Select(g => g.First())
            .OrderBy(v => v.Item1)
            .ToList();
    }

    // Cuenta_Aprobada es un bloqueo duro (a diferencia de Cuenta_En_Trabajo, que expira solo a los
    // 15 minutos): mientras exista la fila, ningún handler de escritura de esta cuenta acepta
    // cambios hasta que un aprobador la reabra (ver CuentasPendientes/Index.cshtml.cs.OnPostReabrirCuentaAsync).
    private async Task<bool> EstaAprobadaAsync(string anioMes, string nroCuenta) =>
        await _db.CuentaAprobada.AnyAsync(a => a.AnioMes == anioMes && a.NroCuenta == nroCuenta);

    // ValueCountLimit default de ASP.NET Core es 1024 campos de formulario -- cada línea de
    // la grilla postea 6 (Lineas[i].Id/.CodCentroCosto/.CodCanal/.CodSucursal + 2 para
    // .EsGastoIndirecto, checkbox + hidden), TODAS las líneas, no solo las que el buscador
    // de texto deja visibles (ese filtro es solo cliente, ver buscadorLineas en el .cshtml).
    // Una cuenta con más de ~170 líneas (ej. cuentas de rendición/contabilización con
    // cientos de movimientos) supera el límite y Kestrel corta la request con un 400 crudo
    // ANTES de llegar al handler -- bug real encontrado 2026-08-11 con la cuenta
    // 61-08-001-11 en el piloto de Comercial Depor (otras cuentas más chicas, ej. Sueldos
    // con 70+ líneas, no lo disparaban). 8000 no alcanzó a resolverlo en producción (según
    // diagnóstico posterior, ver docs/11-ESTADO-PILOTO-DESARROLLO.md) -- subido a 200000 y
    // sumados KeyLength/ValueLength generosos para descartar cualquier otro límite de
    // FormOptions de una vez.
    [RequestFormLimits(ValueCountLimit = 200000, KeyLengthLimit = 4096, ValueLengthLimit = 1048576)]
    public async Task<IActionResult> OnPostAplicarAsignacionAsync(string anioMes, string nroCuenta, AsignacionMasivaInput input)
    {
        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
        if (cierre?.Estado == "CERRADO")
        {
            MensajeError = "El mes está cerrado: no se pueden guardar cambios.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        if (await EstaAprobadaAsync(anioMes, nroCuenta))
        {
            MensajeError = "La cuenta está aprobada y bloqueada: pedile al aprobador que la reabra para modificarla.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        var solicitudes = (input.Lineas ?? new()).ToDictionary(l => l.Id, l => l);
        if (solicitudes.Count == 0)
        {
            MensajeError = "Debes seleccionar al menos una línea.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        var lineas = await _db.DistribucionFinal
            .Where(d => solicitudes.Keys.Contains(d.Id))
            .ToListAsync();

        // Como ahora todas las líneas (pendientes y ya guardadas) viajan siempre en el POST con su valor
        // actual preseleccionado, hay que comparar contra lo que ya está en la base para saber qué cambió
        // de verdad — si no, cada "Guardar" volvería a timestampear y a pisar el comentario de TODA la cuenta.
        var maestroSucursal = await _db.MaestroSucursal.Where(m => m.Activo).ToListAsync();
        var nombresCC = (await ObtenerDimension(anioMes, "CentroCosto", maestroSucursal)).ToDictionary(v => v.Codigo, v => v.Nombre);
        var nombresCanal = (await ObtenerDimension(anioMes, "Canal", maestroSucursal)).ToDictionary(v => v.Codigo, v => v.Nombre);
        var nombresSuc = (await ObtenerDimension(anioMes, "Sucursal", maestroSucursal)).ToDictionary(v => v.Codigo, v => v.Nombre);

        int actualizadas = 0;
        foreach (var linea in lineas)
        {
            var pedido = solicitudes[linea.Id];

            bool cambioCC = !string.IsNullOrEmpty(pedido.CodCentroCosto) && pedido.CodCentroCosto != linea.CodCentroCostoDestino;
            bool cambioCanal = !string.IsNullOrEmpty(pedido.CodCanal) && pedido.CodCanal != linea.CodCanalDestino;
            bool cambioSuc = !string.IsNullOrEmpty(pedido.CodSucursal) && pedido.CodSucursal != linea.CodSucursalDestino;
            bool cambioIndirecto = pedido.EsGastoIndirecto != linea.EsGastoIndirecto;

            if (!cambioCC && !cambioCanal && !cambioSuc && !cambioIndirecto)
                continue;

            if (cambioCC)
            {
                linea.CodCentroCostoDestino = pedido.CodCentroCosto;
                linea.CentroCostoDestino = nombresCC.GetValueOrDefault(pedido.CodCentroCosto!, "");
            }
            if (cambioCanal)
            {
                linea.CodCanalDestino = pedido.CodCanal;
                linea.CanalDestino = nombresCanal.GetValueOrDefault(pedido.CodCanal!, "");
            }
            if (cambioSuc)
            {
                linea.CodSucursalDestino = pedido.CodSucursal;
                linea.SucursalDestino = nombresSuc.GetValueOrDefault(pedido.CodSucursal!, "");
            }
            if (cambioIndirecto)
            {
                linea.EsGastoIndirecto = pedido.EsGastoIndirecto;
            }

            linea.TipoOrigen = "MANUAL";
            linea.UsuarioResponsable = NombreUsuarioActual;
            linea.FechaAsignacion = DateTime.Now;
            linea.Comentario = input.Comentario;
            linea.Estado = "APROBADO";
            actualizadas++;
        }

        if (actualizadas == 0)
        {
            MensajeError = "No hay cambios que guardar: elegí un centro de costo, canal, sucursal o marca de gasto indirecto distinto al que ya tenía cada línea.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        await _db.SaveChangesAsync();

        MensajeExito = $"Se aplicó la asignación a {actualizadas} línea(s).";
        return RedirectToPage(new { anioMes, nroCuenta });
    }

    // Reparte manualmente el monto de UNA línea (un StagingId) en N partidas con destino y
    // monto propios -- para cuentas como Sueldos que llegan en un solo asiento y no tienen
    // ninguna Regla_Distribucion/base de venta que justifique un reparto automático (a
    // diferencia de sp_EjecutarDistribucionAutomatica, acá el humano decide caso a caso).
    // Distribucion_Final ya soporta varias filas por StagingId -- es el mismo patrón que usa
    // el motor AUTO -- así que no hace falta ninguna tabla/columna nueva.
    //
    // Ya NO exige que las partidas calcen exacto con el monto original: lo que no se reparte
    // explícitamente queda automáticamente como una partida más, con destino = el
    // centro de costo/canal/sucursal ORIGINAL de la línea y Estado=PENDIENTE (igual que una
    // línea recién llegada, sin tocar) -- esto es lo que hace posible guardar la división de a
    // poco (una partida hoy, otra mañana) y que la cuenta siga apareciendo como "pendiente"
    // por ese saldo en Index.cshtml.cs (que cuenta por Estado=="PENDIENTE"), sin cambios ahí.
    public async Task<IActionResult> OnPostDividirLineaAsync(string anioMes, string nroCuenta, DivisionLineaInput input)
    {
        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
        if (cierre?.Estado == "CERRADO")
        {
            MensajeError = "El mes está cerrado: no se pueden guardar cambios.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        if (await EstaAprobadaAsync(anioMes, nroCuenta))
        {
            MensajeError = "La cuenta está aprobada y bloqueada: pedile al aprobador que la reabra para modificarla.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        // Filas en blanco que el usuario dejó en el modal (agregó "+ Agregar partida" pero no
        // llegó a cargarle monto) se descartan en silencio -- no son un error, simplemente no
        // aportan nada a la división.
        var partidas = (input.Partidas ?? new()).Where(p => p.Monto > 0).ToList();
        if (partidas.Count == 0)
        {
            MensajeError = "Elegí al menos un destino con un monto mayor a 0 para dividir esta línea.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        if (partidas.Any(p => string.IsNullOrEmpty(p.CodCentroCosto) && string.IsNullOrEmpty(p.CodCanal) && string.IsNullOrEmpty(p.CodSucursal)))
        {
            MensajeError = "Cada partida necesita al menos un centro de costo, canal o sucursal de destino.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        var existentes = await _db.DistribucionFinal
            .Where(d => d.StagingId == input.StagingId && d.AnioMes == anioMes && d.NroCuenta == nroCuenta)
            .ToListAsync();

        if (existentes.Count == 0)
        {
            MensajeError = "No se encontró la línea a dividir.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        var original = existentes[0];
        var sumaPartidas = partidas.Sum(p => p.Monto);
        var remanente = original.MontoOriginal - sumaPartidas;
        // Sobre-repartido: las partidas explícitas suman más de lo que hay (sin importar el
        // signo del monto original -- una cuenta con reverso/nota de crédito puede venir en
        // negativo, y ahí "sobrepartir" es que el remanente se pase para el otro lado del cero).
        var sobrepartido = original.MontoOriginal >= 0 ? remanente < 0 : remanente > 0;
        if (sobrepartido)
        {
            MensajeError = $"Las partidas suman ${sumaPartidas:N0}, más de lo que hay para repartir (${original.MontoOriginal:N0}).";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        var maestroSucursal = await _db.MaestroSucursal.Where(m => m.Activo).ToListAsync();
        var nombresCC = (await ObtenerDimension(anioMes, "CentroCosto", maestroSucursal)).ToDictionary(v => v.Codigo, v => v.Nombre);
        var nombresCanal = (await ObtenerDimension(anioMes, "Canal", maestroSucursal)).ToDictionary(v => v.Codigo, v => v.Nombre);
        var nombresSuc = (await ObtenerDimension(anioMes, "Sucursal", maestroSucursal)).ToDictionary(v => v.Codigo, v => v.Nombre);

        var ahora = DateTime.Now;
        var nuevas = partidas.Select(p =>
        {
            var codCC = string.IsNullOrEmpty(p.CodCentroCosto) ? original.CodCentroCostoOriginal : p.CodCentroCosto;
            var codCanal = string.IsNullOrEmpty(p.CodCanal) ? original.CodCanalOriginal : p.CodCanal;
            var codSuc = string.IsNullOrEmpty(p.CodSucursal) ? original.CodSucursalOriginal : p.CodSucursal;

            return new DistribucionFinal
            {
                StagingId = original.StagingId,
                NroAsiento = original.NroAsiento,
                LineaId = original.LineaId,
                AnioMes = original.AnioMes,
                Fecha = original.Fecha,
                NroCuenta = original.NroCuenta,
                NombreCuenta = original.NombreCuenta,
                CodCentroCostoOriginal = original.CodCentroCostoOriginal,
                CentroCostoOriginal = original.CentroCostoOriginal,
                CodCanalOriginal = original.CodCanalOriginal,
                CanalOriginal = original.CanalOriginal,
                CodSucursalOriginal = original.CodSucursalOriginal,
                SucursalOriginal = original.SucursalOriginal,
                MontoOriginal = original.MontoOriginal,
                CodCentroCostoDestino = codCC,
                CentroCostoDestino = string.IsNullOrEmpty(codCC) ? "" : nombresCC.GetValueOrDefault(codCC, ""),
                CodCanalDestino = codCanal,
                CanalDestino = string.IsNullOrEmpty(codCanal) ? "" : nombresCanal.GetValueOrDefault(codCanal, ""),
                CodSucursalDestino = codSuc,
                SucursalDestino = string.IsNullOrEmpty(codSuc) ? "" : nombresSuc.GetValueOrDefault(codSuc, ""),
                MontoDistribuido = p.Monto,
                TipoOrigen = "MANUAL",
                ReglaAplicada = "División manual",
                UsuarioResponsable = NombreUsuarioActual,
                FechaAsignacion = ahora,
                Comentario = input.Comentario,
                Estado = "APROBADO",
                EsGastoIndirecto = input.EsGastoIndirecto,
            };
        }).ToList();

        var cantidadPartidasExplicitas = nuevas.Count;
        if (remanente != 0)
        {
            // El saldo no repartido queda "en el origen" -- misma forma que una línea recién
            // llegada sin tocar (ver el SIN_AJUSTE que arma OnPostDeshacerDivisionAsync más
            // abajo), para que Index.cshtml.cs la siga contando como pendiente por este monto.
            nuevas.Add(new DistribucionFinal
            {
                StagingId = original.StagingId,
                NroAsiento = original.NroAsiento,
                LineaId = original.LineaId,
                AnioMes = original.AnioMes,
                Fecha = original.Fecha,
                NroCuenta = original.NroCuenta,
                NombreCuenta = original.NombreCuenta,
                CodCentroCostoOriginal = original.CodCentroCostoOriginal,
                CentroCostoOriginal = original.CentroCostoOriginal,
                CodCanalOriginal = original.CodCanalOriginal,
                CanalOriginal = original.CanalOriginal,
                CodSucursalOriginal = original.CodSucursalOriginal,
                SucursalOriginal = original.SucursalOriginal,
                MontoOriginal = original.MontoOriginal,
                CodCentroCostoDestino = original.CodCentroCostoOriginal,
                CentroCostoDestino = original.CentroCostoOriginal,
                CodCanalDestino = original.CodCanalOriginal,
                CanalDestino = original.CanalOriginal,
                CodSucursalDestino = original.CodSucursalOriginal,
                SucursalDestino = original.SucursalOriginal,
                MontoDistribuido = remanente,
                TipoOrigen = "SIN_AJUSTE",
                Estado = "PENDIENTE",
            });
        }

        try
        {
            _db.DistribucionFinal.RemoveRange(existentes);
            _db.DistribucionFinal.AddRange(nuevas);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        MensajeExito = remanente != 0
            ? $"Línea dividida en {cantidadPartidasExplicitas} partida(s); quedan ${Math.Abs(remanente):N0} sin asignar en el origen."
            : $"Línea dividida en {cantidadPartidasExplicitas} partida(s).";
        return RedirectToPage(new { anioMes, nroCuenta });
    }

    // Colapsa las N partidas de una división manual de vuelta a una sola línea SIN_AJUSTE,
    // tal como si nunca se hubiera dividido -- para corregir un error sin editar partida por
    // partida (ver PartidaDivisionInput/OnPostDividirLineaAsync).
    public async Task<IActionResult> OnPostDeshacerDivisionAsync(string anioMes, string nroCuenta, int stagingId)
    {
        var cierre = await _db.CierreMes.FirstOrDefaultAsync(c => c.AnioMes == anioMes);
        if (cierre?.Estado == "CERRADO")
        {
            MensajeError = "El mes está cerrado: no se pueden guardar cambios.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        if (await EstaAprobadaAsync(anioMes, nroCuenta))
        {
            MensajeError = "La cuenta está aprobada y bloqueada: pedile al aprobador que la reabra para modificarla.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        var existentes = await _db.DistribucionFinal
            .Where(d => d.StagingId == stagingId && d.AnioMes == anioMes && d.NroCuenta == nroCuenta)
            .ToListAsync();

        // Una división puede haber quedado en UNA sola fila (repartida 100% a un destino
        // nuevo, sin remanente en origen) -- existentes.Count por sí solo no alcanza para
        // distinguir eso de "nunca se tocó" (también 1 fila), así que hace falta mirar
        // TipoOrigen: solo está "sin dividir de verdad" si esa única fila sigue SIN_AJUSTE.
        var noHayNadaQueDeshacer = existentes.Count == 1 && existentes[0].TipoOrigen != "MANUAL";
        if (noHayNadaQueDeshacer)
        {
            MensajeError = "Esta línea no está dividida: no hay nada que deshacer.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        var staging = await _db.StagingCentralizacion.FindAsync(stagingId);
        if (staging is null)
        {
            MensajeError = "No se encontró la línea original en el staging.";
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        try
        {
            _db.DistribucionFinal.RemoveRange(existentes);
            _db.DistribucionFinal.Add(new DistribucionFinal
            {
                StagingId = staging.Id,
                NroAsiento = staging.NroAsiento,
                LineaId = staging.LineaId,
                AnioMes = staging.AnioMes,
                Fecha = staging.Fecha,
                NroCuenta = staging.NroCuenta,
                NombreCuenta = staging.NombreCuenta,
                CodCentroCostoOriginal = staging.CodCentroCosto,
                CentroCostoOriginal = staging.CentroCosto,
                CodCanalOriginal = staging.CodCanal,
                CanalOriginal = staging.Canal,
                CodSucursalOriginal = staging.CodSucursal,
                SucursalOriginal = staging.Sucursal,
                MontoOriginal = staging.MontoNeto,
                CodCentroCostoDestino = staging.CodCentroCosto,
                CentroCostoDestino = staging.CentroCosto,
                CodCanalDestino = staging.CodCanal,
                CanalDestino = staging.Canal,
                CodSucursalDestino = staging.CodSucursal,
                SucursalDestino = staging.Sucursal,
                MontoDistribuido = staging.MontoNeto,
                TipoOrigen = "SIN_AJUSTE",
                Estado = "PENDIENTE",
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            MensajeError = ObtenerMensajeError(ex);
            return RedirectToPage(new { anioMes, nroCuenta });
        }

        MensajeExito = "División deshecha: la línea vuelve a estar pendiente de asignación.";
        return RedirectToPage(new { anioMes, nroCuenta });
    }
}
