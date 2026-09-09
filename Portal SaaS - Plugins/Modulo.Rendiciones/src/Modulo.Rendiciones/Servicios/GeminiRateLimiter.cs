namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Limitador de velocidad (RPM) en memoria para las llamadas a Google Gemini, por
/// proveedor (API key). La capa gratuita de la familia Flash tolera ~10-15
/// solicitudes por minuto; pasarse devuelve HTTP 429. Este guardrail deja de mandar
/// solicitudes ANTES de comerse el 429, para "estar dentro de los límites gratis".
///
/// Ventana deslizante de 60 s. Es <b>por proceso</b> -- con varias instancias del Host
/// (IIS scale-out) el tope efectivo se multiplica; aceptable para el uso esperado
/// (OCR de comprobantes, no un batch masivo). Singleton (ver ModuloRendiciones.RegisterServices).
/// </summary>
public sealed class GeminiRateLimiter
{
    /// <summary>
    /// Tope conservador dentro de la capa gratuita (Gemini 2.5 Flash: 10-15 RPM). Si se
    /// pasa a modalidad de pago se puede subir -- por ahora la meta es no salir del
    /// nivel gratuito.
    /// </summary>
    public const int MaxRequestsPerMinute = 10;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Dictionary<long, Queue<DateTimeOffset>> _hitsByProvider = new();
    private readonly object _gate = new();

    /// <summary>
    /// Registra un intento de llamada para <paramref name="providerId"/> y devuelve true
    /// si queda dentro del tope por minuto. False = hay que esperar unos segundos.
    /// </summary>
    public bool TryAcquire(long providerId)
    {
        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (!_hitsByProvider.TryGetValue(providerId, out var hits))
            {
                hits = new Queue<DateTimeOffset>();
                _hitsByProvider[providerId] = hits;
            }

            while (hits.Count > 0 && now - hits.Peek() > Window)
            {
                hits.Dequeue();
            }

            if (hits.Count >= MaxRequestsPerMinute)
            {
                return false;
            }

            hits.Enqueue(now);
            return true;
        }
    }
}
