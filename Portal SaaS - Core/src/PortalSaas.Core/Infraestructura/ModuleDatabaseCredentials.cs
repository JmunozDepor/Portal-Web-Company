using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Infraestructura;

/// <summary>
/// Credencial efectiva de la base de un módulo: la propia del binding si la tiene, si no
/// la de su Instance. Requiere binding.Instance cargado (Include).
/// </summary>
internal static class ModuleDatabaseCredentials
{
    public static (string Username, string Password) Resolve(CompanyModuleConnection binding, ISecretoCifradoService secretos)
    {
        var tienePropia = !string.IsNullOrEmpty(binding.TechnicalSecretKey);
        var username = tienePropia && !string.IsNullOrWhiteSpace(binding.TechnicalUsername)
            ? binding.TechnicalUsername!
            : binding.Instance.TechnicalUsername;
        var cifrada = tienePropia ? binding.TechnicalSecretKey! : binding.Instance.TechnicalSecretKey;
        var password = string.IsNullOrEmpty(cifrada) ? "" : secretos.Decrypt(cifrada);
        return (username, password);
    }
}
