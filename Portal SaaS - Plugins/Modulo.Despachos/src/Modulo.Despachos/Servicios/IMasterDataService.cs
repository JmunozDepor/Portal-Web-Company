using Modulo.Despachos.Models;

namespace Modulo.Despachos.Servicios;

/// <summary>Resultado de eliminar un maestro: si hay dependientes no se borra y se explica por qué.</summary>
public sealed record DeleteResult(bool Deleted, string? BlockedReason);

/// <summary>
/// Mantenedores del módulo (transportistas, vehículos, choferes, zonas...). Todo se acota a la
/// compañía activa (ICurrentCompanyAccessor). Save* inserta si Id == 0, si no actualiza; las
/// violaciones de reglas lanzan InvalidOperationException con un mensaje apto para el usuario.
/// </summary>
public interface IMasterDataService
{
    /// <summary>RUT de la compañía como emisor de DTE (normalizado), o null si no está configurado.</summary>
    Task<string?> GetIssuerTaxIdAsync();

    /// <summary>Guarda el RUT emisor (validado, dígito verificador); vacío lo borra.</summary>
    Task SaveIssuerTaxIdAsync(string? rut);

    /// <summary>Ajustes del módulo de la compañía (null si nunca se guardaron).</summary>
    Task<DespachosSetting?> GetSettingsAsync();

    /// <summary>
    /// Guías de despacho Res. 154: activar la emisión al despachar, IndTraslado (1..9) y TipoDespacho (1..3 o null).
    /// </summary>
    Task SaveGuideSettingsAsync(bool guidesEnabled, int transferIndicator, int? dispatchType, string lineGrouping = GuideLineGroupings.Sku,
        string itemCodePlacement = GuideItemCodePlacements.Column, int maxLines = 60);

    Task<IReadOnlyList<Carrier>> ListCarriersAsync(bool includeInactive = false);
    Task<long> SaveCarrierAsync(Carrier entity);
    Task<DeleteResult> DeleteCarrierAsync(long id);

    Task<IReadOnlyList<VehicleType>> ListVehicleTypesAsync(bool includeInactive = false);
    Task<long> SaveVehicleTypeAsync(VehicleType entity);
    Task<DeleteResult> DeleteVehicleTypeAsync(long id);

    Task<IReadOnlyList<Vehicle>> ListVehiclesAsync(bool includeInactive = false);
    Task<long> SaveVehicleAsync(Vehicle entity);
    Task<DeleteResult> DeleteVehicleAsync(long id);

    Task<IReadOnlyList<Driver>> ListDriversAsync(bool includeInactive = false);
    Task<long> SaveDriverAsync(Driver entity);
    Task<DeleteResult> DeleteDriverAsync(long id);

    Task<IReadOnlyList<Shift>> ListShiftsAsync(bool includeInactive = false);
    Task<long> SaveShiftAsync(Shift entity);
    Task<DeleteResult> DeleteShiftAsync(long id);

    /// <summary>Zonas con sus comunas (Zone.Communes). Save sincroniza las comunas con la lista recibida.</summary>
    Task<IReadOnlyList<Zone>> ListZonesAsync(bool includeInactive = false);
    Task<long> SaveZoneAsync(Zone entity);
    Task<DeleteResult> DeleteZoneAsync(long id);

    Task<IReadOnlyList<Origin>> ListOriginsAsync(bool includeInactive = false);
    Task<long> SaveOriginAsync(Origin entity);
    Task<DeleteResult> DeleteOriginAsync(long id);

    Task<IReadOnlyList<RejectionReason>> ListRejectionReasonsAsync(bool includeInactive = false);
    Task<long> SaveRejectionReasonAsync(RejectionReason entity);
    Task<DeleteResult> DeleteRejectionReasonAsync(long id);

    /// <summary>Los feriados no tienen estado activo: includeInactive se ignora.</summary>
    Task<IReadOnlyList<Holiday>> ListHolidaysAsync(bool includeInactive = false);
    Task<long> SaveHolidayAsync(Holiday entity);
    Task<DeleteResult> DeleteHolidayAsync(long id);

    Task<IReadOnlyList<NotificationRecipient>> ListNotificationRecipientsAsync(bool includeInactive = false);
    Task<long> SaveNotificationRecipientAsync(NotificationRecipient entity);
    Task<DeleteResult> DeleteNotificationRecipientAsync(long id);

    /// <summary>Catálogo de comunas de la compañía (sembrado por CommuneSeeder), ordenado por región y nombre.</summary>
    Task<IReadOnlyList<Commune>> ListCommunesAsync();

    /// <summary>
    /// Busca una comuna por nombre: ignora tildes, mayúsculas, puntuación y abreviaturas típicas (ver
    /// CommuneNames) y usa las equivalencias de la compañía para los textos que SAP trae mal digitados.
    /// </summary>
    Task<Commune?> FindCommuneByNameAsync(string name);

    /// <summary>Equivalencias de comuna de la compañía (texto de SAP → comuna del catálogo).</summary>
    Task<IReadOnlyList<CommuneAlias>> ListCommuneAliasesAsync();
    Task<long> SaveCommuneAliasAsync(CommuneAlias entity);
    Task<DeleteResult> DeleteCommuneAliasAsync(long id);

    /// <summary>
    /// Recuerda que <paramref name="sourceText"/> (texto de SAP) es la comuna <paramref name="communeId"/>: crea o
    /// actualiza la equivalencia. No hace nada si el texto está vacío o ya se reconoce solo.
    /// </summary>
    Task RememberCommuneAliasAsync(string? sourceText, long communeId);
}
