using System.Collections.Concurrent;

namespace Modulo.FacturasProveedor.Servicios;

/// <summary>
/// Un ciclo a la vez por compañía en este proceso: la evaluación (job o "Leer ahora" / "Asociar OC" desde la web) y los
/// vencimientos/envíos del job. Dos ciclos en paralelo creaban la misma revisión dos veces o chocaban en el sello de
/// concurrencia (error 500 en la página). No es reentrante: ningún ciclo llama a otro.
/// </summary>
internal static class CompanyCycleLock
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    public static async Task<IDisposable> AcquireAsync(Guid companyId, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(companyId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Releaser(gate);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _gate;

        public Releaser(SemaphoreSlim gate) => _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
