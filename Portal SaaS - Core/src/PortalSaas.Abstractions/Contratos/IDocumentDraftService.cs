namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Borradores de documentos digitados (Venta/Compra/Inventario) antes de enviarlos a SAP
/// -- ver docs/superpowers/plans/2026-10-20-borradores-documentos.md. Acotado a la
/// compañía activa de la sesión. Todos los usuarios de la compañía ven todos los
/// borradores (el listado filtra por defecto los propios); los permisos sobre el menú
/// los valida la página.
/// </summary>
public interface IDocumentDraftService
{
    /// <summary>Crea (Id null) o actualiza un borrador pendiente. Devuelve su Id. Falla si ya fue enviado o se está enviando.</summary>
    Task<Guid> SaveAsync(DocumentDraftSaveRequest request, CancellationToken ct = default);

    Task<DocumentDraftDto?> GetAsync(Guid id, CancellationToken ct = default);

    Task<DocumentDraftListResult> ListAsync(DocumentDraftQuery query, CancellationToken ct = default);

    /// <summary>Elimina un borrador que no esté enviándose. Avisa por la campana al creador si lo elimina otro usuario.</summary>
    Task DeleteAsync(Guid id, string? notifyUrl = null, CancellationToken ct = default);

    /// <summary>
    /// Paso atómico draft|error -> sending (token de concurrencia). False si otro proceso ya
    /// lo tomó o ya fue enviado. Un "sending" sin cambios hace más de 10 minutos (el
    /// servidor se cayó a mitad) se puede volver a tomar -- revisar en SAP antes.
    /// </summary>
    Task<bool> TryBeginSendAsync(Guid id, CancellationToken ct = default);

    /// <summary>Marca enviado con el DocEntry creado en SAP. Avisa al creador si lo envió otro usuario.</summary>
    Task MarkSentAsync(Guid id, int docEntry, string? notifyUrl = null, CancellationToken ct = default);

    /// <summary>Vuelve a pendiente con el error de SAP, para corregir y reintentar.</summary>
    Task MarkErrorAsync(Guid id, string errorMessage, CancellationToken ct = default);
}

/// <summary>Parámetros de comportamiento de documentos por compañía (hoy: retención de borradores).</summary>
public interface ICompanyDocumentSettingsService
{
    Task<int?> GetDraftRetentionDaysAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>Solo admin, compañía de la organización actual. Null o &lt;= 0 = no vencen.</summary>
    Task SaveDraftRetentionDaysAsync(Guid companyId, int? days, CancellationToken ct = default);

    /// <summary>UDF de cabecera para la marca de origen (ver IDocumentSapMarkerService). Null = Comentarios.</summary>
    Task<string?> GetDraftMarkerUdfNameAsync(Guid companyId, CancellationToken ct = default);

    /// <summary>Solo admin. Formato "U_" + letras/números/guion bajo (se valida: se usa en SQL). Vacío = Comentarios.</summary>
    Task SaveDraftMarkerUdfNameAsync(Guid companyId, string? udfName, CancellationToken ct = default);
}

public static class DocumentDraftEngine
{
    public const string Sales = "ventas";
    public const string Purchase = "compras";
    public const string Inventory = "inventario";
}

public static class DocumentDraftStatus
{
    public const string Draft = "draft";
    public const string Sending = "sending";
    public const string Sent = "sent";
    public const string Error = "error";

    public static string Label(string status) => status switch
    {
        Draft => "Borrador",
        Sending => "Enviando",
        Sent => "Enviado",
        Error => "Con error",
        _ => status,
    };

    public static bool IsEditable(string status) => status is Draft or Error;
}

public sealed record DocumentDraftSaveRequest(
    Guid? Id,
    string Engine,
    string DocumentType,
    string Reason,
    string PayloadJson,
    string? PartnerCode,
    string? Reference,
    DateOnly? DocDate,
    int LineCount,
    decimal? Total);

public sealed record DocumentDraftDto(
    Guid Id,
    string Engine,
    string DocumentType,
    string Status,
    string Reason,
    string PayloadJson,
    string? PartnerCode,
    string? Reference,
    DateOnly? DocDate,
    int LineCount,
    decimal? Total,
    Guid CreatedByUserId,
    string CreatedByUsername,
    string UpdatedByUsername,
    int? SapDocEntry,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SentAt);

/// <param name="OnlyMine">True = solo los creados por el usuario actual (filtro por defecto del listado).</param>
/// <param name="StatusGroup">"pendientes" (borrador/con error/enviando) | "enviados" | "todos".</param>
public sealed record DocumentDraftQuery(
    string Engine,
    string DocumentType,
    bool OnlyMine = true,
    string StatusGroup = "pendientes",
    int Page = 1,
    int PageSize = 25);

public sealed record DocumentDraftListResult(IReadOnlyList<DocumentDraftDto> Items, int TotalRecords);
