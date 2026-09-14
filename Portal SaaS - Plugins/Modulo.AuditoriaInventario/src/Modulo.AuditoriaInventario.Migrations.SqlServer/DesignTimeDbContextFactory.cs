using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulo.AuditoriaInventario.Data;

namespace Modulo.AuditoriaInventario.Migrations.SqlServer;

/// <summary>
/// Factory de diseño para generar/aplicar las migraciones SQL Server de
/// AuditoriaInventarioDbContext (motor que resuelve Comercial Depor, cliente
/// on-premise). Nunca se usa en runtime real -- ver el comentario equivalente en
/// Migrations.Postgres/DesignTimeDbContextFactory.cs.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuditoriaInventarioDbContext>
{
    public AuditoriaInventarioDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AUDITORIAINVENTARIO_CONNECTION_STRING")
            ?? "Server=localhost;Database=modulo_auditoria_inventario_dev;User Id=portalsaas;Password=portalsaas_dev_only;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<AuditoriaInventarioDbContext>()
            .UseSqlServer(connectionString, x => x.MigrationsAssembly("Modulo.AuditoriaInventario.Migrations.SqlServer"));

        return new AuditoriaInventarioDbContext(optionsBuilder.Options);
    }
}
