using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// Consume IGenericImportJobQueue y llama IGenericImportService.CreateDocumentsAsync
/// fuera del hilo de request HTTP -- antes OnPostConfirmAsync (Pages/Importar/Index)
/// esperaba el proceso completo dentro del propio request, bloqueando el hilo de IIS
/// por minutos con archivos grandes. El progreso lo sigue reportando
/// CreateDocumentsAsync exactamente igual que antes (IGenericImportProgressStore,
/// consultado por polling desde el cliente) -- este servicio no cambia esa lógica,
/// solo QUIÉN y CUÁNDO la llama. Mismo patrón de scope que
/// LicenseActivatorBackgroundService (IServiceScopeFactory, porque los servicios de
/// negocio son Scoped y este es un Singleton de larga vida).
/// </summary>
public sealed class GenericImportBackgroundService(
    IGenericImportJobQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<GenericImportBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await queue.DequeueAsync(stoppingToken);

            using var scope = scopeFactory.CreateScope();
            var importService = scope.ServiceProvider.GetRequiredService<IGenericImportService>();

            try
            {
                await importService.CreateDocumentsAsync(job.JobId, job.PortalUsername, job.Parameters, job.Documents, stoppingToken);
            }
            catch (Exception ex)
            {
                // CreateDocumentsAsync ya reporta error por documento en el progreso
                // (ver GenericImportService) -- este catch es solo para un fallo
                // catastrófico fuera de ese try interno (ej. el propio DbContext sin
                // poder abrir conexión), para que UN trabajo roto no tumbe el
                // BackgroundService completo y deje de procesar los siguientes.
                logger.LogError(ex, "Fallo no controlado procesando el trabajo de importación {JobId}.", job.JobId);
            }
        }
    }
}
