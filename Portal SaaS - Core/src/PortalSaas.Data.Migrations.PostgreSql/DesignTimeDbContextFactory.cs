using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using PortalSaas.Data;

namespace PortalSaas.Data.Migrations.PostgreSql;

/// <summary>
/// Factory de diseño para generar/aplicar las migraciones de PostgreSQL de
/// PortalSaasDbContext. Vive en este proyecto (no en PortalSaas.Data) porque
/// `dotnet ef` necesita que la fábrica y el ensamblado de migraciones coincidan --
/// ver docs/02-ARQUITECTURA-BASE-DE-DATOS.md §7 (motor dual) y el hermano
/// PortalSaas.Data.Migrations.SqlServer para el mismo patrón con SQL Server.
///
/// Nunca se usa en runtime real -- el Host futuro registra PortalSaasDbContext por
/// DI con su propio mecanismo de configuración/secretos, igual criterio que
/// PortalSAP_v2.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PortalSaasDbContext>
{
    public PortalSaasDbContext CreateDbContext(string[] args)
    {
        // Sin prefijo -- ver el comentario equivalente en el DesignTimeDbContextFactory
        // hermano de PortalSaas.Data.Migrations.SqlServer (bug real 2026-08-08, mismatch
        // de nombre de variable de entorno con el resto del proyecto).
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration["ConnectionStrings:Default"]
            ?? "Host=localhost;Port=5432;Database=portalsaas_dev;Username=portalsaas;Password=portalsaas_dev_only";

        var optionsBuilder = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseNpgsql(connectionString, x => x.MigrationsAssembly("PortalSaas.Data.Migrations.PostgreSql"))
            .UseSnakeCaseNamingConvention();

        return new PortalSaasDbContext(optionsBuilder.Options);
    }
}
