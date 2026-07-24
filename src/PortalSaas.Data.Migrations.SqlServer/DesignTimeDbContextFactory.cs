using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using PortalSaas.Data;

namespace PortalSaas.Data.Migrations.SqlServer;

/// <summary>
/// Factory de diseño para generar/aplicar las migraciones de SQL Server de
/// PortalSaasDbContext -- usada, por ejemplo, para una instalación on-premise que ya
/// tiene SQL Server (ej. Comercial Depor en sqlsap.cdepor.cl), sin instalar Postgres
/// ahí. Ver el hermano PortalSaas.Data.Migrations.PostgreSql para el mismo patrón.
///
/// Nunca se usa en runtime real -- el Host futuro registra PortalSaasDbContext por
/// DI con su propio mecanismo de configuración/secretos, igual criterio que
/// PortalSAP_v2.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PortalSaasDbContext>
{
    public PortalSaasDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables(prefix: "PORTALSAAS_")
            .Build();

        var connectionString = configuration["ConnectionStrings:Default"]
            ?? "Server=localhost;Database=portalsaas_dev;User Id=portalsaas;Password=portalsaas_dev_only;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseSqlServer(connectionString, x => x.MigrationsAssembly("PortalSaas.Data.Migrations.SqlServer"))
            .UseSnakeCaseNamingConvention();

        return new PortalSaasDbContext(optionsBuilder.Options);
    }
}
