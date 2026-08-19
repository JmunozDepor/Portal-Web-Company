using System.Globalization;
using B1SLayer;
using Microsoft.Extensions.Options;
using Servicios.Common.Configuracion;
using Servicios.Common.Contratos;
using Servicios.TipoCambioBancoCentral.BancoCentral;
using Servicios.TipoCambioBancoCentral.Configuracion;
using Servicios.TipoCambioBancoCentral.Contratos;
using Servicios.TipoCambioBancoCentral.Sap;

namespace Servicios.TipoCambioBancoCentral;

/// <summary>
/// Orquestador principal, portado tal cual del servicio legado (Service1.EjecutarProceso):
/// (1) salvavidas e-commerce si SAP quedó en 0 hoy, (2) pre-carga de seguridad nocturna
/// después de las 22:00, (3) regla retroactiva que rellena feriados/fines de semana con el
/// Tipo de Cambio oficial del Banco Central. Ninguna de las tres reglas cambia al portar --
/// ver TODO explícito solo donde aplica (no hay TODO acá, el algoritmo se porta completo).
///
/// Diferencia de plomería respecto al legado: el legado era single-tenant (un solo
/// SLConnection fijo por instancia del servicio, credenciales en appsettings.json en texto
/// plano, Timer.FromHours). Acá cada compañía activa (ITipoCambioCompanyProvider) corre de
/// forma aislada -- una excepción en una compañía no aborta el resto del batch, mismo
/// criterio que Worker de TransferenciaAutomatica -- las credenciales de Service Layer y
/// del Banco Central se descifran recién al momento de usarlas (ISecretoCifradoService), y
/// el intervalo del ciclo es WorkerOptions.CicloIntervaloSegundos en vez de un TimeSpan
/// hardcodeado.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ITipoCambioCompanyProvider _companyProvider;
    private readonly ISecretoCifradoService _secretoCifradoService;
    private readonly ILogSink _logSink;
    private readonly ILogger<Worker> _logger;
    private readonly BancoCentralClient _bancoCentralClient;
    private readonly BancoCentralOptions _bancoCentralOptions;
    private readonly WorkerOptions _options;

    public Worker(
        ITipoCambioCompanyProvider companyProvider,
        ISecretoCifradoService secretoCifradoService,
        ILogSink logSink,
        ILogger<Worker> logger,
        BancoCentralClient bancoCentralClient,
        IOptions<BancoCentralOptions> bancoCentralOptions,
        IOptions<WorkerOptions> options)
    {
        _companyProvider = companyProvider;
        _secretoCifradoService = secretoCifradoService;
        _logSink = logSink;
        _logger = logger;
        _bancoCentralClient = bancoCentralClient;
        _bancoCentralOptions = bancoCentralOptions.Value;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var companias = await _companyProvider.GetActiveCompaniesAsync(stoppingToken);
            _logger.LogInformation("Iniciando ciclo de tipo de cambio ({Cantidad} compañías activas)", companias.Count);

            foreach (var compania in companias)
            {
                stoppingToken.ThrowIfCancellationRequested();

                try
                {
                    await EjecutarProcesoDeCompaniaAsync(compania, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Falló el ciclo de tipo de cambio para {CompanyCode}", compania.CompanyCode);
                    await _logSink.WriteAsync(new LogEntry
                    {
                        FechaHora = DateTimeOffset.UtcNow,
                        Nivel = NivelLog.Error,
                        CompanyCode = compania.CompanyCode,
                        Mensaje = "Falló el ciclo de sincronización de tipo de cambio",
                        Detalle = ex.ToString()
                    }, stoppingToken);
                }
            }

            await Task.Delay(_options.CicloIntervalo, stoppingToken);
        }
    }

    private async Task EjecutarProcesoDeCompaniaAsync(TipoCambioCompanyConfig compania, CancellationToken ct)
    {
        var serviceLayer = new SLConnection(
            compania.ServiceLayerUrl,
            compania.ServiceLayerDb,
            compania.ServiceLayerUsername,
            _secretoCifradoService.Descifrar(compania.ServiceLayerSecretCifrado));

        var sap = new SapCurrencyRateClient(serviceLayer, _logger);
        string bcoUrl = _bancoCentralOptions.Url;
        string bcoUser = _bancoCentralOptions.Usuario;
        string bcoPass = _secretoCifradoService.Descifrar(_bancoCentralOptions.SecretoCifrado);

        DateTime fechaActual = DateTime.Now;
        string fechaHoy = fechaActual.ToString("yyyyMMdd");

        // =========================================================================
        // SALVAVIDAS E-COMMERCE (corre en cada ciclo si SAP está en 0)
        // =========================================================================
        decimal? tasaSAPHoy = await sap.ConsultarAsync(fechaHoy);
        if (tasaSAPHoy == null || tasaSAPHoy == 0)
        {
            DateTime diaAnterior = fechaActual.AddDays(-1);
            if (fechaActual.DayOfWeek == DayOfWeek.Monday) diaAnterior = fechaActual.AddDays(-3);

            decimal? tasaAyer = await sap.ConsultarAsync(diaAnterior.ToString("yyyyMMdd"));
            if (tasaAyer != null && tasaAyer > 0)
            {
                await sap.InsertarAsync(fechaActual.ToString("dd-MM-yyyy"), tasaAyer.Value.ToString());
                _logger.LogInformation(
                    "[E-COMMERCE PROTEGIDO] {CompanyCode}: SAP estaba en 0 a las {Hora}. Se cargó temporalmente TC anterior ({Tasa})",
                    compania.CompanyCode, fechaActual.ToString("HH:mm"), tasaAyer);
            }
        }

        // =========================================================================
        // PRE-CARGA DE SEGURIDAD NOCTURNA (después de las 22:00)
        // =========================================================================
        if (fechaActual.Hour >= 22)
        {
            DateTime fechaManana = fechaActual.AddDays(1);
            decimal? tasaSAPManana = await sap.ConsultarAsync(fechaManana.ToString("yyyyMMdd"));
            decimal? tasaHoyActual = await sap.ConsultarAsync(fechaHoy);

            // REGLA DE VALIDACIÓN: solo inserta si el valor de mañana en SAP es DIFERENTE al valor de hoy
            if (tasaHoyActual != null && tasaHoyActual > 0 && tasaSAPManana != tasaHoyActual)
            {
                await sap.InsertarAsync(fechaManana.ToString("dd-MM-yyyy"), tasaHoyActual.Value.ToString());
                _logger.LogInformation(
                    "[PRE-CARGA NOCTURNA] {CompanyCode}: Son las {Hora}. Se pre-cargó el TC de mañana ({Manana}) con el valor de hoy ({Tasa}) para asegurar la transición de medianoche.",
                    compania.CompanyCode, fechaActual.ToString("HH:mm"), fechaManana.ToString("dd-MM-yyyy"), tasaHoyActual);
            }
        }

        // =========================================================================
        // REGLA DE NEGOCIO RETROACTIVA
        // =========================================================================
        // Rango de 10 días hacia atrás para evaluar feriados o fines de semana
        DateTime fechaInicio = fechaActual.AddDays(-10);
        var datosBC = await _bancoCentralClient.ConsultarRangoAsync(bcoUrl, bcoUser, bcoPass, fechaInicio, fechaActual, ct);

        if (datosBC == null)
        {
            return;
        }

        var diasSinTipoCambio = new List<DateTime>();

        for (DateTime dia = fechaInicio; dia <= fechaActual; dia = dia.AddDays(1))
        {
            string fechaKey = dia.ToString("dd-MM-yyyy");

            // Si el Banco Central tiene un valor válido para este día
            if (datosBC.TryGetValue(fechaKey, out var valorOficialString) &&
                !string.IsNullOrEmpty(valorOficialString) &&
                valorOficialString.Trim().ToUpper() != "NAN" &&
                BancoCentralClient.TryParseValorOficial(valorOficialString, out decimal valorOficialDecimal))
            {
                // 3.1 VALIDACIÓN DEL DÍA OFICIAL: ¿lo que tiene SAP es igual al valor real del BC?
                decimal? tasaActualSAP = await sap.ConsultarAsync(dia.ToString("yyyyMMdd"));

                if (tasaActualSAP != valorOficialDecimal)
                {
                    // Si es diferente (o es un placeholder temporal), se sobrescribe con el oficial
                    await sap.InsertarAsync(fechaKey, valorOficialString);
                }

                // 3.2 VALIDACIÓN RETROACTIVA (fines de semana / feriados acumulados)
                if (diasSinTipoCambio.Count > 0)
                {
                    foreach (var diaFeriado in diasSinTipoCambio)
                    {
                        decimal? tasaFeriadoSAP = await sap.ConsultarAsync(diaFeriado.ToString("yyyyMMdd"));

                        // Comparamos el valor de SAP contra el valorOficialDecimal que DEBERÍA tener:
                        // si el sábado tiene el valor provisional del viernes, será diferente al lunes
                        // oficial, por ende entrará acá, lo corregirá y mandará el log. En la siguiente
                        // hora, como ya serán IGUALES, lo ignorará por completo.
                        if (tasaFeriadoSAP != valorOficialDecimal)
                        {
                            await sap.InsertarAsync(diaFeriado.ToString("dd-MM-yyyy"), valorOficialString);
                            _logger.LogInformation(
                                "[REGLA APLICADA] {CompanyCode}: feriado/fin de semana ({Dia}) actualizado con el TC posterior del día {FechaKey}: {Valor}",
                                compania.CompanyCode, diaFeriado.ToString("dd-MM-yyyy"), fechaKey, valorOficialString);
                        }
                    }
                    diasSinTipoCambio.Clear();
                }
            }
            else
            {
                // Es un fin de semana o feriado según el BC, lo acumulamos
                diasSinTipoCambio.Add(dia);
            }
        }
    }
}
