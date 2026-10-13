using System.Text.Json;
using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Core.Sap;

/// <summary>
/// Interpreta la respuesta del $batch de SapSession.SimulatePostAsync -- separado de la
/// llamada HTTP para poder testearlo sin Service Layer.
///
/// El changeset lleva 2 operaciones: Content-ID 1 = el POST del documento, Content-ID 2 =
/// un PATCH a un artículo que no puede existir (fuerza el rollback). Service Layer,
/// cuando un changeset falla, revierte todo y devuelve la respuesta de la operación que
/// falló:
/// - falla la 2 (o la 1 vino con éxito) -> SAP ACEPTÓ el documento -> Valid.
/// - falla la 1 -> SAP rechazó el documento, el mensaje es el error real -> Rejected.
/// Si la respuesta no trae Content-ID, se distingue por el código de error del centinela
/// (-2028 "No matching records found"), que un POST de documento no devuelve.
/// </summary>
internal static class SapSimulationResponseInterpreter
{
    internal const int SentinelErrorCode = -2028;

    internal sealed record BatchPart(int StatusCode, string? ContentId, string Body);

    internal static (SapSimulationOutcome Outcome, string Message) Interpret(IReadOnlyList<BatchPart> parts)
    {
        if (parts.Count == 0)
        {
            return (SapSimulationOutcome.Inconclusive, "Service Layer no devolvió ninguna respuesta para el envío simulado.");
        }

        var failures = parts.Where(p => p.StatusCode >= 400).ToList();
        if (failures.Count == 0)
        {
            // El centinela no puede tener éxito -- si todo vino OK algo no se comportó como
            // se esperaba y el documento PODRÍA haber quedado creado.
            return (SapSimulationOutcome.Inconclusive,
                "Respuesta inesperada: todas las operaciones del envío simulado tuvieron éxito. Revisá en SAP que no haya quedado un documento creado.");
        }

        var failure = failures[0];
        var (code, message) = ReadError(failure.Body);
        // El centinela nunca tiene éxito -- cualquier parte exitosa es el documento.
        var documentSucceeded = parts.Any(p => p.StatusCode < 400);

        if (documentSucceeded || failure.ContentId == "2" || (failure.ContentId is null && code == SentinelErrorCode))
        {
            return (SapSimulationOutcome.Valid, "SAP aceptó el documento (envío simulado, revertido -- no se creó ningún registro).");
        }

        return (SapSimulationOutcome.Rejected, message ?? $"SAP rechazó el documento (HTTP {failure.StatusCode}).");
    }

    /// <summary>{"error":{"code":-10,"message":{"lang":"en-us","value":"..."}}} (OData v3) o "message":"..." (v4).</summary>
    internal static (int? Code, string? Message) ReadError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var json = JsonDocument.Parse(body);
            if (!json.RootElement.TryGetProperty("error", out var error))
            {
                return (null, body.Trim());
            }

            int? code = error.TryGetProperty("code", out var codeElement)
                ? codeElement.ValueKind == JsonValueKind.Number ? codeElement.GetInt32()
                : int.TryParse(codeElement.GetString(), out var parsed) ? parsed : null
                : null;

            string? message = null;
            if (error.TryGetProperty("message", out var messageElement))
            {
                message = messageElement.ValueKind == JsonValueKind.Object && messageElement.TryGetProperty("value", out var value)
                    ? value.GetString()
                    : messageElement.ValueKind == JsonValueKind.String ? messageElement.GetString() : null;
            }

            return (code, message);
        }
        catch (JsonException)
        {
            return (null, body.Trim());
        }
    }
}
