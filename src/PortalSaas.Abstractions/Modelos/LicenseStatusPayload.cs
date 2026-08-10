namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Estado de una licencia on-premise en el momento en que el central lo firmó -- ver
/// ILicenseTokenService. Esto es lo que CheckLicenseAsync valida, no las columnas
/// Status/ExpiresAt en crudo de la BD local (esas se pueden editar a mano o restaurar
/// desde un backup; un payload sin firma válida no).
/// </summary>
public sealed record LicenseStatusPayload(
    Guid OrganizationId,
    long PlanId,
    int? UserLimit,
    int? CompanyLimit,
    int? MonthlyTransactionLimit,
    string Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset IssuedAt);
