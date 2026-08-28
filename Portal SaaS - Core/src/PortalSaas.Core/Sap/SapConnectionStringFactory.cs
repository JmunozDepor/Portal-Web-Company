using Microsoft.Data.SqlClient;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data.Entities;

namespace PortalSaas.Core.Sap;

/// <summary>
/// Arma la cadena de conexión directa (HANA/SQL Server) a la base SAP B1 de una
/// Company, a partir de su Instance -- lógica compartida entre HanaService (resuelve la
/// compañía activa vía ICurrentCompanyAccessor) y SapConnectionTestService (recibe la
/// compañía explícita, para el botón "Probar conexión" del backoffice, que no tiene
/// sesión de tenant).
/// </summary>
internal static class SapConnectionStringFactory
{
    public static (string EngineType, string ConnectionString) Build(Company company, ISecretoCifradoService secrets)
    {
        if (!company.Instance.IsActive)
        {
            throw new InvalidOperationException($"Instancia '{company.Instance.Name}' de la compañía '{company.Code}' no está activa.");
        }

        var password = secrets.Decrypt(company.Instance.TechnicalSecretKey);

        // company.DatabaseName es el "Current Schema" en HANA o el nombre de la base en
        // SQL Server, según company.Instance.EngineType -- mismo campo, sentido distinto
        // por motor (ver Entities/Company.cs).
        var connectionString = company.Instance.EngineType switch
        {
            InstanceEngineType.Hana =>
                $"Server={company.Instance.Host}:{company.Instance.Port};UserID={company.Instance.TechnicalUsername};Password={password};Current Schema={company.DatabaseName}",
            InstanceEngineType.SqlServer => new SqlConnectionStringBuilder
            {
                DataSource = $"{company.Instance.Host},{company.Instance.Port}",
                InitialCatalog = company.DatabaseName,
                UserID = company.Instance.TechnicalUsername,
                Password = password,
                // El SQL Server de una compañía SAP B1 externa puede usar un certificado
                // no confiado por la cadena del SO (self-signed/CA interna) -- mismo
                // hallazgo/fix que PortalSAP_v2.
                TrustServerCertificate = true,
            }.ConnectionString,
            _ => throw new InvalidOperationException($"Motor de instancia no soportado para lectura directa de SAP B1: {company.Instance.EngineType}"),
        };

        return (company.Instance.EngineType, connectionString);
    }
}
