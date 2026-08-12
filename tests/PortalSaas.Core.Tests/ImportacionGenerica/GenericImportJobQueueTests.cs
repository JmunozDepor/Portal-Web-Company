using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.ImportacionGenerica;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

public class GenericImportJobQueueTests
{
    [Fact]
    public async Task Enqueue_seguido_de_DequeueAsync_devuelve_el_mismo_trabajo()
    {
        var queue = new GenericImportJobQueue();
        var parameters = new GenericImportParametersDto(GenericImportModule.Sales, "Order", GenericImportLineType.Item, null);
        var request = new GenericImportJobRequest("job-1", "usuario_prueba", parameters, []);

        queue.Enqueue(request);
        var dequeued = await queue.DequeueAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("job-1", dequeued.JobId);
        Assert.Equal("usuario_prueba", dequeued.PortalUsername);
    }

    [Fact]
    public async Task DequeueAsync_respeta_el_orden_FIFO_de_varios_trabajos_encolados()
    {
        var queue = new GenericImportJobQueue();
        var parameters = new GenericImportParametersDto(GenericImportModule.Sales, "Order", GenericImportLineType.Item, null);
        queue.Enqueue(new GenericImportJobRequest("job-a", "u", parameters, []));
        queue.Enqueue(new GenericImportJobRequest("job-b", "u", parameters, []));

        var first = await queue.DequeueAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        var second = await queue.DequeueAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("job-a", first.JobId);
        Assert.Equal("job-b", second.JobId);
    }
}
