using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Catálogo de Centro de Costos (OPRC, DimCode=1) de la compañía SAP activa -- usado
/// por las líneas de tipo Servicio de Venta/Compra. Solo la dimensión 1 -- el mapeo
/// de DimCode es específico del ambiente original (1=Centro de Costo/2=Marca/
/// 5=Tipo de Gasto, confirmado contra Comercial Depor en referencia-original/
/// PortalSAP_v2), reconfirmar contra el ambiente real de cada organización antes de
/// asumir que aplica igual -- Dimensión 2/Dimensión 3 quedan sin portar (mismo
/// criterio YAGNI que el resto del proyecto -- opcionales en el original, no
/// bloquean crear una línea de Servicio).
/// </summary>
public interface ICostCenterCatalogService
{
    Task<IReadOnlyList<CostCenterDto>> ListAsync(string? searchText = null, int? limit = null, CancellationToken ct = default);

    /// <summary>
    /// Dimensión 2/3 (Marca/Tipo de Gasto en el ambiente original, ver la advertencia
    /// de la clase) -- mismo OPRC, filtrado por un DimCode explícito distinto de 1.
    /// </summary>
    Task<IReadOnlyList<CostCenterDto>> ListByDimensionAsync(int dimCode, string? searchText = null, int? limit = null, CancellationToken ct = default);
}
