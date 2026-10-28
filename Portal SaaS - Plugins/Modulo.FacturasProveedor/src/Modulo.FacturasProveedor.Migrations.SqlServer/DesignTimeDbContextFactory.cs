using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulo.FacturasProveedor.Data;

namespace Modulo.FacturasProveedor.Migrations.SqlServer;

/// <summary>
/// Factory de diseño para generar/aplicar las migraciones SQL Server de
/// FacturasProveedorDbContext. Nunca se usa en runtime real -- el plugin registra el
/// DbContext por DI vía IExternalDatabaseConnectionService (ver
/// ModuloDespachos.RegisterServices, motor resuelto en runtime).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FacturasProveedorDbContext>
{
    public FacturasProveedorDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FACTURAS_PROVEEDOR_CONNECTION_STRING")
            ?? "Server=localhost;Database=modulo_facturas_proveedor_dev;User Id=portalsaas;Password=portalsaas_dev_only;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<FacturasProveedorDbContext>()
            .UseSqlServer(connectionString, x => x.MigrationsAssembly("Modulo.FacturasProveedor.Migrations.SqlServer"));

        return new FacturasProveedorDbContext(optionsBuilder.Options);
    }
}

