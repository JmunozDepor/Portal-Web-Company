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
        // Sin prefijo -- bug real (2026-08-08): con prefix: "PORTALSAAS_", la variable
        // de entorno tenía que llamarse "PORTALSAAS_ConnectionStrings__Default", pero
        // TODO el resto del proyecto (Program.cs, build-all.ps1, docs/11-ESTADO-PILOTO-
        // DESARROLLO.md) usa el nombre estándar de ASP.NET Core sin prefijo
        // ("ConnectionStrings__Default"). El mismatch hacía que esta factory nunca
        // encontrara la variable, cayera siempre al fallback hardcodeado de abajo
        // (Server=localhost, inexistente en este equipo) y fallara con "Named Pipes
        // Provider, error 40" sin importar qué connection string se seteara -- el
        // usuario nunca estaba conectando de verdad a sqlsap.cdepor.cl.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration["ConnectionStrings:Default"]
            ?? "Server=localhost;Database=portalsaas_dev;User Id=portalsaas;Password=portalsaas_dev_only;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseSqlServer(connectionString, x => x.MigrationsAssembly("PortalSaas.Data.Migrations.SqlServer"))
            .UseSnakeCaseNamingConvention();

        return new PortalSaasDbContext(optionsBuilder.Options);
    }
}
