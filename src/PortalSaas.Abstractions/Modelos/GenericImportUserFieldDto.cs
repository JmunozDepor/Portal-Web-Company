namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Fila del catálogo maestro de campos de usuario de la organización -- solo
/// administrador de organización. Un campo de usuario es siempre un valor plano
/// (texto/número/fecha) que viaja tal cual hacia Service Layer bajo SapFieldName, sin
/// ninguna lógica de resolución contra SAP -- puede ser un UDF propio o cualquier
/// propiedad estándar de Service Layer que todavía no sea un campo núcleo del motor.
/// Portado de CampoUsuarioImportacionGenericaDto. Ver GenericImportLogicalField.UserField
/// y SapAdditionalFieldsHelper.IsReservedName (bloquea que esto pise un campo núcleo).
/// </summary>
public sealed record GenericImportUserFieldDto(
    int Id,
    GenericImportModule Module,
    GenericImportFieldLevel Level,
    string Label,
    string SapFieldName,
    GenericImportFieldDataType DataType,
    bool IsActive);
