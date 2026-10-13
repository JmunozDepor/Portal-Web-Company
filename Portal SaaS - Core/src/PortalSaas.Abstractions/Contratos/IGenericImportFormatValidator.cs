using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Validación de un formato de Importación Genérica (GenericImportConfigDto) contra el
/// SAP de la compañía activa, SIN crear documentos -- pedido del dueño del proyecto
/// (2026-10-09): probar el formato desde Configuración antes de usarlo en Importar.
/// </summary>
public interface IGenericImportFormatValidator
{
    /// <summary>
    /// Sin archivo: campos obligatorios mapeados para el módulo/tipo de línea, campos
    /// mapeados que el módulo ignora, letras de columna, valores fijos contra los
    /// catálogos SAP, y cada campo de usuario U_ contra CUFD (tabla de cabecera/línea del
    /// tipo de documento, tipo de dato).
    /// </summary>
    Task<IReadOnlyList<GenericImportFormatCheckDto>> ValidateSchemaAsync(int configId, CancellationToken ct = default);

    /// <summary>
    /// Con un Excel de ejemplo: lo de ValidateSchemaAsync + vista previa procesada con
    /// ESTE formato + envío SIMULADO del JSON del primer documento sin errores (Service
    /// Layer lo valida completo y lo revierte, no queda registro -- ver
    /// ISapSession.SimulatePostAsync). businessPartnerCardCode: socio para probar un
    /// formato estándar (sin socio propio); se ignora si el formato ya tiene socio.
    /// </summary>
    Task<GenericImportFormatTestResultDto> TestWithFileAsync(int configId, Stream file, string portalUsername,
        string? businessPartnerCardCode, CancellationToken ct = default);
}
