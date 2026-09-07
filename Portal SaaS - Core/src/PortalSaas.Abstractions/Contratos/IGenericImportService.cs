using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Motor de importación masiva de documentos Venta/Compra/Inventario desde un archivo
/// Excel estándar por columnas (ver GenericImportConfigDto). Portado de
/// IImportacionGenericaService -- NUNCA postea directo a Service Layer: arma
/// SalesDocumentLineDto/PurchaseDocumentLineDto/InventoryDocumentLineDto y llama
/// ISalesDocumentService/IPurchaseDocumentService/IInventoryDocumentService.CreateAsync,
/// el mismo camino que usa la creación manual desde la UI -- así hereda gratis UDF de
/// trazabilidad y override de "permite crear" por organización, sin duplicar esa lógica.
/// </summary>
public interface IGenericImportService
{
    /// <summary>
    /// Lee el archivo, resuelve la configuración vigente (estándar o excepción del
    /// socio de negocio), cruza cada fila contra los catálogos, agrupa en documentos
    /// (ver GenericImportConfigDto.GroupingColumn) y corre el pipeline de reglas de
    /// validación. No crea nada en SAP todavía -- es la vista previa antes de confirmar.
    /// </summary>
    Task<GenericImportResultDto> ProcessFileAsync(GenericImportParametersDto parameters, Stream file, CancellationToken ct = default);

    /// <summary>
    /// Crea en SAP los documentos ya validados (ver GenericImportDocumentDto.CanCreate),
    /// uno por grupo, siempre vía I*DocumentService.CreateAsync -- actualiza el progreso
    /// en IGenericImportProgressStore bajo la clave jobId mientras corre (el llamador
    /// arranca esto en el mismo request que después consulta por polling).
    /// </summary>
    Task<GenericImportProgressDto> CreateDocumentsAsync(string jobId, string portalUsername,
        GenericImportParametersDto parameters, IReadOnlyList<GenericImportDocumentDto> documents, CancellationToken ct = default);

    /// <summary>
    /// Genera el .xlsx de "Descargar formato": si hay una GenericImportConfigDto
    /// vigente (estándar o excepción del socio) para Organización+Module+DocumentType+
    /// LineType, el encabezado sale de esa configuración. Si no hay ninguna, arma un
    /// layout genérico por defecto (columnas A, B, C... en orden, solo los campos
    /// núcleo que aplican a ese Module/LineType) para que igual haya algo para
    /// completar antes de configurar el mapeo real.
    /// </summary>
    Task<byte[]> GenerateTemplateAsync(GenericImportParametersDto parameters, CancellationToken ct = default);

    /// <summary>
    /// Reconstruye el archivo ya procesado (mismas columnas y mismas letras de la
    /// configuración vigente, en el mismo orden de fila del Excel original -- ver
    /// GenericImportRowDto.RawValues, el valor tal cual lo tipeó el usuario, no el ya
    /// resuelto contra SAP) agregando una columna "Errores" al final con la bitácora de
    /// cada fila (vacía si es válida). Pensado para corregir y volver a subir sin tener
    /// que ir fila por fila comparando contra la vista previa en pantalla. Portado de
    /// IImportacionGenericaService.GenerarArchivoConErroresAsync.
    /// </summary>
    Task<byte[]> GenerateFileWithErrorsAsync(GenericImportParametersDto parameters,
        IReadOnlyList<GenericImportDocumentDto> documents, CancellationToken ct = default);

    /// <summary>
    /// Reporte de validación PRE-carga -- distinto del reporte de resultado post-carga
    /// (ver docs/superpowers/specs/2026-09-06-reporte-resultado-importacion-generica-
    /// design.md). Se genera a partir de la vista previa (ProcessFileAsync), disponible
    /// sin haber confirmado nada -- hoja "Detalle" (una fila por línea, con Errores y
    /// Advertencias) + hoja "Stock por artículo-bodega" (una fila por cada par
    /// (ItemCode, Bodega) marcado por la regla StockAvailable).
    /// </summary>
    Task<byte[]> GenerateValidationReportAsync(GenericImportParametersDto parameters,
        IReadOnlyList<GenericImportDocumentDto> documents, CancellationToken ct = default);
}
