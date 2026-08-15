using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Host.Wms;

public static class WmsInboundEndpoints
{
    private const long MaxBodySizeBytes = 10 * 1024 * 1024;

    public static void MapWmsInboundEndpoints(this WebApplication app)
    {
        app.MapPost("/api/wms/inbound/receive", HandleAsync)
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes("ExternalApiKey").RequireAuthenticatedUser());
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        IWmsInboundIngestionService ingestionService,
        CancellationToken cancellationToken)
    {
        var companyIdClaim = httpContext.User.FindFirst("CompanyId")?.Value;
        if (companyIdClaim is null || !Guid.TryParse(companyIdClaim, out var companyId))
        {
            return Results.Unauthorized();
        }

        var contentType = httpContext.Request.ContentType ?? string.Empty;
        string formato;
        if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            formato = "Xml";
        }
        else if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            formato = "Json";
        }
        else if (contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
        {
            formato = "Txt";
        }
        else
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (formato != "Xml")
        {
            return Results.BadRequest(new
            {
                success = false,
                message = $"Formato '{formato}' reconocido pero sin parser implementado todavía — solo XML soportado en esta ronda.",
            });
        }

        using var memoryStream = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(memoryStream, cancellationToken);
        var rawBody = memoryStream.ToArray();

        if (rawBody.Length == 0)
        {
            return Results.BadRequest(new { success = false, message = "El cuerpo de la petición está vacío." });
        }

        if (rawBody.LongLength > MaxBodySizeBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var xmlContent = Encoding.UTF8.GetString(rawBody);

        System.Xml.Linq.XDocument xmlDoc;
        try
        {
            xmlDoc = SecureXmlHelper.ParseSecurely(xmlContent);
        }
        catch (XmlException)
        {
            return Results.BadRequest(new { success = false, message = "El XML está mal formado o contiene elementos no permitidos (DOCTYPE/DTD no soportado)." });
        }

        var messageId = SecureXmlHelper.GetValue(xmlDoc.Root, "Header", "MessageId");
        var entity = SecureXmlHelper.GetValue(xmlDoc.Root, "Header", "Entity");

        if (string.IsNullOrWhiteSpace(entity))
        {
            return Results.BadRequest(new { success = false, message = "Falta el campo obligatorio Header/Entity." });
        }

        if (!string.Equals(entity, "shipped_load", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { success = false, message = $"Entity '{entity}' no está permitida en esta ronda (solo 'shipped_load' → SLSH)." });
        }

        var nombreArchivo = string.IsNullOrWhiteSpace(messageId)
            ? $"{Guid.NewGuid():N}.xml"
            : $"{SanitizeForFileName(messageId)}.xml";
        var hashArchivo = Convert.ToHexString(SHA256.HashData(rawBody)).ToLowerInvariant();

        var resultado = await ingestionService.InsertPendingAsync(
            companyId, "SLSH", formato, nombreArchivo, hashArchivo, xmlContent, cancellationToken);

        if (resultado.Duplicado)
        {
            return Results.Ok(new { success = true, message = "Mensaje ya recibido previamente (idempotente)." });
        }

        return Results.Ok(new { success = true, message = "Recibido y encolado correctamente.", nombreArchivo });
    }

    private static string SanitizeForFileName(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                sb.Append(c);
            }
        }
        var cleaned = sb.ToString();
        if (cleaned.Length == 0)
        {
            cleaned = Guid.NewGuid().ToString("N")[..12];
        }
        return cleaned.Length > 100 ? cleaned[..100] : cleaned;
    }
}
