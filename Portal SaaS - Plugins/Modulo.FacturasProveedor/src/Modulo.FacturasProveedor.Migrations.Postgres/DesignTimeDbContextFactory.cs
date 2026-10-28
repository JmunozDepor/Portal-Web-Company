using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Modulo.FacturasProveedor.Data;

namespace Modulo.FacturasProveedor.Migrations.Postgres;

/// <summary>
/// Factory de diseño para generar/aplicar las migraciones Postgres de
/// FacturasProveedorDbContext. Nunca se usa en runtime real -- el plugin registra el
/// DbContext por DI vía IExternalDatabaseConnectionService (ver
/// ModuloDespachos.RegisterServices, motor resuelto en runtime).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FacturasProveedorDbContext>
{
    public FacturasProveedorDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FACTURAS_PROVEEDOR_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=modulo_facturas_proveedor_dev;Username=portalsaas;Password=portalsaas_dev_only";

        var optionsBuilder = new DbContextOptionsBuilder<FacturasProveedorDbContext>()
            .UseNpgsql(connectionString, x => x.MigrationsAssembly("Modulo.FacturasProveedor.Migrations.Postgres"));

        return new FacturasProveedorDbContext(optionsBuilder.Options);
    }
}

