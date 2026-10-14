namespace Modulo.AuditoriaInventario.Servicios;

public sealed record SapSyncEstado(
    bool EnCurso,
    int Procesados,
    DateTimeOffset IniciadoEn,
    DateTimeOffset? TerminadoEn,
    string? Mensaje,
    bool Error);

/// <summary>
/// Estado en memoria de la sincronización con SAP por compañía -- permite que
/// "Sincronizar desde SAP" corra en segundo plano (Task.Run con su propio scope,
/// ver MaestroProductos/Index.cshtml.cs) sin dejar la pantalla del admin "pegada"
/// mientras dura (pedido explícito, 2026-10-14: con ~200K artículos la
/// sincronización sincrónica tardaba varios minutos). Singleton: vive mientras
/// viva el proceso del Host -- se pierde si se reinicia, pero eso es aceptable,
/// es solo progreso visual; los productos ya quedan guardados en la base a medida
/// que se procesan los lotes, nunca se pierde trabajo real.
/// </summary>
public interface ISapSyncStatusTracker
{
    SapSyncEstado? Obtener(Guid companyId);

    /// <summary>False si ya había una sincronización en curso para esa compañía -- no la reinicia.</summary>
    bool IntentarIniciar(Guid companyId);

    void ActualizarProgreso(Guid companyId, int procesados);
    void Completar(Guid companyId, string mensaje);
    void Fallar(Guid companyId, string mensaje);
}

public sealed class SapSyncStatusTracker : ISapSyncStatusTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, SapSyncEstado> _estados = new();

    public SapSyncEstado? Obtener(Guid companyId)
    {
        lock (_lock)
        {
            return _estados.GetValueOrDefault(companyId);
        }
    }

    public bool IntentarIniciar(Guid companyId)
    {
        lock (_lock)
        {
            if (_estados.TryGetValue(companyId, out var actual) && actual.EnCurso)
            {
                return false;
            }

            _estados[companyId] = new SapSyncEstado(true, 0, DateTimeOffset.UtcNow, null, null, false);
            return true;
        }
    }

    public void ActualizarProgreso(Guid companyId, int procesados)
    {
        lock (_lock)
        {
            if (_estados.TryGetValue(companyId, out var actual) && actual.EnCurso)
            {
                _estados[companyId] = actual with { Procesados = procesados };
            }
        }
    }

    public void Completar(Guid companyId, string mensaje)
    {
        lock (_lock)
        {
            if (_estados.TryGetValue(companyId, out var actual))
            {
                _estados[companyId] = actual with { EnCurso = false, TerminadoEn = DateTimeOffset.UtcNow, Mensaje = mensaje, Error = false };
            }
        }
    }

    public void Fallar(Guid companyId, string mensaje)
    {
        lock (_lock)
        {
            if (_estados.TryGetValue(companyId, out var actual))
            {
                _estados[companyId] = actual with { EnCurso = false, TerminadoEn = DateTimeOffset.UtcNow, Mensaje = mensaje, Error = true };
            }
        }
    }
}
