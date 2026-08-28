namespace PortalSaas.Core.Correo;

/// <summary>
/// `HttpResponseMessage.EnsureSuccessStatusCode()` descarta el cuerpo de la
/// respuesta -- inútil para depurar errores de Graph/Gmail API, que siempre
/// devuelven el motivo real en el body (ej. "domainPolicy", "insufficientPermissions").
/// Encontrado en la práctica: un 403 sin este detalle no dice nada de por qué.
/// </summary>
internal static class HttpResponseValidation
{
    public static async Task EnsureSuccessWithBodyAsync(this HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException(
            $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}",
            inner: null,
            statusCode: response.StatusCode);
    }
}
