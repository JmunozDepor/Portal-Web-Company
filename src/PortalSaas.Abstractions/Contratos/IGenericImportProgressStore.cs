using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>Store persistido del progreso de trabajos de importación genérica -- ver IGenericImportService.CreateDocumentsAsync. Portado de IImportacionGenericaProgresoStore.</summary>
public interface IGenericImportProgressStore
{
    Task UpdateAsync(string jobId, GenericImportProgressDto progress);

    Task<GenericImportProgressDto?> GetAsync(string jobId);
}
