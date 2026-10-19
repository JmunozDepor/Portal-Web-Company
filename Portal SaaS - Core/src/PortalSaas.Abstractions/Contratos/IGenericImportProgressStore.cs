using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Store en memoria (singleton) del progreso de trabajos de importación genérica -- ver IGenericImportService.CreateDocumentsAsync. Portado de IImportacionGenericaProgresoStore.</summary>
public interface IGenericImportProgressStore
{
    void Update(string jobId, GenericImportProgressDto progress);

    GenericImportProgressDto? Get(string jobId);

    /// <summary>Libera el progreso de un trabajo terminado (ya quedó guardado en la base) -- implementación por defecto no-op.</summary>
    void Remove(string jobId)
    {
    }
}
