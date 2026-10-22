using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;

namespace Modulo.Despachos.Servicios;

/// <summary>Calcula y persiste el costo de un manifiesto con el tarifario vigente.</summary>
public interface IManifestCostService
{
    /// <summary>
    /// Recalcula el costo del manifiesto (que debe estar cargado en el contexto con sus documentos):
    /// reemplaza las líneas de manifest_cost_lines, fija cost_calculated / rate_card_id / cost_status y
    /// recalcula cost_final = cost_override ?? cost_calculated. Con "sin tarifa" o "ambigua"
    /// cost_calculated queda en null. Guarda los cambios pendientes del contexto (una sola escritura).
    /// </summary>
    Task<CostResult> RecalculateAsync(Manifest manifest);

    /// <summary>
    /// Calcula el costo con los datos actuales (documentos, adicionales, tarifas) sin guardar nada: es el
    /// costo estimado que se muestra en un borrador antes de despachar.
    /// </summary>
    Task<CostResult> EstimateAsync(Manifest manifest);
}
