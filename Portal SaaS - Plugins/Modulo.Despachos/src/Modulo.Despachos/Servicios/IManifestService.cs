using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;

namespace Modulo.Despachos.Servicios;

/// <summary>Datos de cabecera editables de un manifiesto en borrador.</summary>
public sealed record ManifestHeaderInput(
    DateOnly DispatchDate, long? ShiftId, long OriginId, long CarrierId, long VehicleId, long? TrailerId, long DriverId,
    DateOnly? EstimatedArrivalDate, TimeOnly? EstimatedArrivalTime, TimeOnly? PlannedDepartureTime,
    string? ServiceOrder, string? Remarks, int? OdometerStart);

/// <summary>Captura de un documento: el texto del escáner (TED) o, a mano, tipo + folio (y opcionalmente el tipo SII).</summary>
public sealed record DocumentCapture(string? RawScan, string? DocumentKind, long? Folio, int? SiiDocumentType = null);

/// <summary>Resultado de entrega de un documento al registrar el retorno.</summary>
public sealed record DocumentReturnInput(Guid ManifestDocumentId, string DeliveryStatus, long? RejectionReasonId, string? Comments);

/// <summary>Valores de AddDocumentResult.Outcome.</summary>
public static class AddDocumentOutcomes
{
    public const string Added = "added";
    public const string DuplicateInThis = "duplicate_in_this";
    public const string InOtherManifest = "in_other_manifest";
    public const string NotFound = "not_found";
    public const string Canceled = "canceled";
    public const string InvalidScan = "invalid_scan";
    public const string OtherIssuer = "other_issuer";
    /// <summary>El folio existe con más de un tipo de documento: la UI debe pedir el tipo.</summary>
    public const string Ambiguous = "ambiguous";
}

public sealed record AddDocumentResult(
    string Outcome, Guid? ManifestDocumentId, int? OtherManifestNumber, IReadOnlyList<string> Warnings);

public sealed record DispatchResult(bool Success, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed record ManifestFilter(
    DateOnly? From = null, DateOnly? To = null, string? Status = null, long? CarrierId = null, string? FolioContains = null);

/// <summary>Adicional con cantidad del tarifario (recargo manual_quantity) que aplica al manifiesto, con su cantidad (0 si no hay).</summary>
public sealed record ManifestExtraChargeRow(long RateSurchargeId, string Name, string EffectType, decimal Value, int Quantity);

public sealed record ManifestListItem(
    Guid Id, int Number, string Status, DateOnly DispatchDate, long CarrierId, string CarrierName, string? Plate,
    int TotalDocuments, decimal? CostFinal, string CostStatus);

/// <summary>
/// Armado y despacho de manifiestos. Todo se acota a la compañía activa; las violaciones de reglas lanzan
/// InvalidOperationException con un mensaje apto para el usuario. Cada transición deja una fila en manifest_events.
/// </summary>
public interface IManifestService
{
    Task<Guid> CreateDraftAsync(ManifestHeaderInput input);
    Task UpdateDraftHeaderAsync(Guid manifestId, ManifestHeaderInput input);
    Task<AddDocumentResult> AddDocumentAsync(Guid manifestId, DocumentCapture capture);
    Task RemoveDocumentAsync(Guid manifestId, Guid manifestDocumentId);

    /// <summary>Valida, sella la salida (UTC), toma el snapshot Res.154, calcula el costo y pasa los documentos a despachado.</summary>
    Task<DispatchResult> DispatchAsync(Guid manifestId);

    /// <summary>
    /// Solo con el manifiesto despachado y con motivo: renueva el snapshot y recalcula el costo. El transportista
    /// del manifiesto nunca cambia; devuelve advertencias (vehículo/chofer de otro transportista).
    /// </summary>
    Task<IReadOnlyList<string>> ChangeVehicleOrDriverAsync(Guid manifestId, long vehicleId, long? trailerId, long driverId, string reason);

    /// <summary>
    /// Registra el resultado de entrega por documento (manifiesto despachado, o retornado para corregirlo antes de
    /// cerrar). Cuando todos los documentos tienen resultado el manifiesto pasa a retornado. Con odómetro final
    /// guarda el km y recalcula el costo. Avisa por la campana los rechazos y entregas parciales.
    /// </summary>
    Task RegisterReturnAsync(Guid manifestId, IReadOnlyList<DocumentReturnInput> documents, int? odometerEnd);

    /// <summary>Anula un manifiesto en borrador o despachado, con motivo; sus documentos quedan libres.</summary>
    Task AnnulAsync(Guid manifestId, string reason);

    /// <summary>Costo manual con motivo (manifiesto despachado, retornado o cerrado con el período abierto); cost_final pasa a ser el override.</summary>
    Task OverrideCostAsync(Guid manifestId, decimal amount, string reason);

    /// <summary>
    /// Cierra un manifiesto retornado. Exige costo: calculado (cost_status ok) o ajustado a mano; si no, el cierre
    /// mensual quedaría bloqueado para siempre.
    /// </summary>
    Task CloseAsync(Guid manifestId);

    /// <summary>
    /// Vuelve a calcular el costo con las tarifas y comunas vigentes (manifiesto despachado, retornado o cerrado, con el
    /// período abierto). Deja el evento cost_recalculated con el antes y el después.
    /// </summary>
    Task<CostResult> RecalculateCostAsync(Guid manifestId);

    /// <summary>
    /// Corrige la comuna de destino de un documento (manifiesto en borrador, despachado o retornado, período abierto).
    /// Si el manifiesto ya no es borrador, recalcula el costo.
    /// </summary>
    Task SetDocumentCommuneAsync(Guid manifestId, Guid manifestDocumentId, long communeId);

    /// <summary>
    /// Adicionales con cantidad que se pueden ingresar en el manifiesto: recargos manual_quantity activos, vigentes a la
    /// fecha de despacho y del transportista del manifiesto (o de todos), más los que ya tienen cantidad guardada.
    /// </summary>
    Task<IReadOnlyList<ManifestExtraChargeRow>> ListExtraChargesAsync(Guid manifestId);

    /// <summary>
    /// Guarda las cantidades de adicionales (0 = quitar; máximo 999) en un manifiesto no anulado con el período abierto.
    /// Deja el evento extra_charges_changed y, si el manifiesto ya no es borrador, recalcula el costo.
    /// </summary>
    Task SetExtraChargesAsync(Guid manifestId, IReadOnlyDictionary<long, int> quantities);

    /// <summary>Manifiesto con documentos, líneas de costo y eventos, o null si no existe.</summary>
    Task<Manifest?> GetAsync(Guid manifestId);

    /// <summary>Manifiesto por su correlativo visible (el que lleva el Code128 del PDF), o null si no existe.</summary>
    Task<Manifest?> GetByNumberAsync(int number);

    Task<IReadOnlyList<ManifestListItem>> ListAsync(ManifestFilter filter);
}
