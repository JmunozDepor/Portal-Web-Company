using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Administracion;

/// <summary>
/// Migrador one-shot idempotente: copia las filas legadas de
/// <see cref="ModuleExternalConnection"/> (module_external_connections) al modelo nuevo
/// -- catálogo <see cref="CompanyExternalConnection"/> + binding
/// <see cref="CompanyModuleConnection"/> -- deduplicando conexiones equivalentes dentro
/// de la misma compañía.
///
/// La tabla legada nunca se modifica ni se borra (seguridad de rollback: sigue en el
/// esquema, solo sin lector de runtime desde Task 6). Correr de nuevo no crea nada
/// nuevo: las filas ya migradas se detectan por el binding
/// (CompanyId, ModuleCode, "Default") ya existente.
/// </summary>
public sealed class LegacyExternalConnectionBackfill(PortalSaasDbContext db, ILogger<LegacyExternalConnectionBackfill> logger)
{
    private const string DefaultPurpose = "Default";

    /// <summary>Devuelve la cantidad de bindings creados en esta corrida.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var legadas = await db.ModuleExternalConnections.AsNoTracking().ToListAsync(ct);
        if (legadas.Count == 0)
        {
            return 0;
        }

        var companyIds = legadas.Select(l => l.CompanyId).Distinct().ToList();

        var bindings = await db.CompanyModuleConnections
            .Where(b => companyIds.Contains(b.CompanyId))
            .ToListAsync(ct);

        var conexiones = await db.CompanyExternalConnections
            .Where(c => companyIds.Contains(c.CompanyId))
            .ToListAsync(ct);

        var creados = 0;

        foreach (var me in legadas)
        {
            var tipo = MapTipo(me.EngineType);
            if (tipo is null)
            {
                logger.LogWarning(
                    "Backfill: engine_type no soportado '{Engine}' en module_external_connections id {Id}, fila omitida",
                    me.EngineType, me.Id);
                continue;
            }

            var bindingExistente = bindings.FirstOrDefault(b =>
                b.CompanyId == me.CompanyId
                && b.ModuleCode == me.ModuleCode
                && b.Purpose == DefaultPurpose);

            if (bindingExistente is not null)
            {
                var equivalente = conexiones.FirstOrDefault(c =>
                    c.CompanyId == me.CompanyId
                    && c.Tipo == tipo
                    && c.Host == me.Host
                    && c.Port == me.Port
                    && c.DatabaseName == me.DatabaseName);

                if (equivalente is null || equivalente.Id != bindingExistente.ConnectionId)
                {
                    logger.LogWarning(
                        "Backfill: binding en conflicto para company {CompanyId} module {ModuleCode}: se conserva {ExistingId}, se ignora {LegacyId}",
                        me.CompanyId, me.ModuleCode, bindingExistente.ConnectionId, me.Id);
                }

                continue;
            }

            var conexion = conexiones.FirstOrDefault(c =>
                c.CompanyId == me.CompanyId
                && c.Tipo == tipo
                && c.Host == me.Host
                && c.Port == me.Port
                && c.DatabaseName == me.DatabaseName);

            if (conexion is null)
            {
                conexion = new CompanyExternalConnection
                {
                    CompanyId = me.CompanyId,
                    Nombre = NombreLibre(conexiones, me.CompanyId, me.ModuleCode),
                    Tipo = tipo,
                    Host = me.Host,
                    Port = me.Port,
                    DatabaseName = me.DatabaseName,
                    TechnicalUsername = me.TechnicalUsername,
                    TechnicalSecretKey = me.TechnicalSecretKey,
                    ConfiguracionExtra = null,
                    IsActive = me.IsActive,
                };
                db.CompanyExternalConnections.Add(conexion);
                conexiones.Add(conexion);
            }

            var binding = new CompanyModuleConnection
            {
                CompanyId = me.CompanyId,
                ModuleCode = me.ModuleCode,
                Purpose = DefaultPurpose,
                Connection = conexion,
            };
            db.CompanyModuleConnections.Add(binding);
            bindings.Add(binding);
            creados++;
        }

        if (creados > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return creados;
    }

    private static string NombreLibre(IEnumerable<CompanyExternalConnection> existentes, Guid companyId, string baseNombre)
    {
        var usados = existentes
            .Where(c => c.CompanyId == companyId)
            .Select(c => c.Nombre)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!usados.Contains(baseNombre))
        {
            return baseNombre;
        }

        for (var sufijo = 2; ; sufijo++)
        {
            var candidato = $"{baseNombre} ({sufijo})";
            if (!usados.Contains(candidato))
            {
                return candidato;
            }
        }
    }

    private static string? MapTipo(string engineType) => engineType switch
    {
        ModuleExternalConnectionEngineType.Postgres => ExternalConnectionType.DbPostgres,
        ModuleExternalConnectionEngineType.SqlServer => ExternalConnectionType.DbSqlServer,
        _ => null,
    };
}
