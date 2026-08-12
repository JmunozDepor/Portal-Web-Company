using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.ImportacionGenerica;

/// <summary>
/// Store del progreso de trabajos de IGenericImportService, persistido en la base
/// propia de la plataforma -- ANTES vivía en memoria de proceso (Singleton +
/// ConcurrentDictionary), lo que rompía apenas hubiera más de una instancia de
/// PortalSaas.Host detrás de un balanceador sin sticky sessions (el upload podía
/// caer en una instancia y la consulta de progreso en otra). Registrado como Scoped
/// (ver Program.cs), consistente con el ciclo de vida de PortalSaasDbContext.
/// </summary>
public sealed class GenericImportProgressStore(PortalSaasDbContext db) : IGenericImportProgressStore
{
    public async Task UpdateAsync(string jobId, GenericImportProgressDto progress)
    {
        var existing = await db.GenericImportJobProgresses.FindAsync(jobId);
        if (existing is null)
        {
            db.GenericImportJobProgresses.Add(new GenericImportJobProgress
            {
                JobId = jobId,
                TotalRows = progress.TotalRows,
                ProcessedRows = progress.ProcessedRows,
                Status = progress.Status,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.TotalRows = progress.TotalRows;
            existing.ProcessedRows = progress.ProcessedRows;
            existing.Status = progress.Status;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    public async Task<GenericImportProgressDto?> GetAsync(string jobId)
    {
        var entity = await db.GenericImportJobProgresses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.JobId == jobId);

        return entity is null
            ? null
            : new GenericImportProgressDto
            {
                TotalRows = entity.TotalRows,
                ProcessedRows = entity.ProcessedRows,
                Status = entity.Status,
            };
    }
}
