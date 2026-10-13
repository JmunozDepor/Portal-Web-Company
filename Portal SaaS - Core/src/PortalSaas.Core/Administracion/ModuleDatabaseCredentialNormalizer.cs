using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.Administracion;

/// <summary>
/// One-shot idempotente, corre al arrancar el Host. La migración
/// InstanceAsModuleDatabaseServer agrupa las conexiones viejas en instancias por
/// (motor, host, puerto, usuario), pero NO puede comparar claves en SQL: AES-GCM cifra la
/// misma clave en textos distintos. Por eso deja como "credencial propia" toda clave cuyo
/// texto cifrado no coincide con el de la instancia. Este paso descifra y borra las que
/// son en realidad la misma clave y el mismo usuario que la instancia. Correrlo de nuevo
/// no cambia nada.
/// </summary>
public sealed class ModuleDatabaseCredentialNormalizer(
    PortalSaasDbContext db,
    ISecretoCifradoService secretos,
    ILogger<ModuleDatabaseCredentialNormalizer> logger)
{
    /// <summary>Devuelve la cantidad de credenciales propias borradas por redundantes.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var conPropia = await db.CompanyModuleConnections
            .Include(b => b.Instance)
            .Where(b => b.TechnicalSecretKey != null)
            .ToListAsync(ct);

        var limpiados = 0;
        foreach (var b in conPropia)
        {
            var mismoUsuario = string.IsNullOrWhiteSpace(b.TechnicalUsername)
                || b.TechnicalUsername == b.Instance.TechnicalUsername;
            if (!mismoUsuario)
            {
                continue;
            }

            string propia;
            string deInstancia;
            try
            {
                propia = secretos.Decrypt(b.TechnicalSecretKey!);
                deInstancia = secretos.Decrypt(b.Instance.TechnicalSecretKey);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Normalizador: no se pudo descifrar la credencial del binding {BindingId} ({ModuleCode}); se conserva.",
                    b.Id, b.ModuleCode);
                continue;
            }

            if (propia != deInstancia)
            {
                continue;
            }

            b.TechnicalUsername = null;
            b.TechnicalSecretKey = null;
            b.UpdatedAt = DateTimeOffset.UtcNow;
            limpiados++;
        }

        if (limpiados > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return limpiados;
    }
}
