using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Data;

namespace Modulo.FacturasProveedor.Tests.Fakes;

/// <summary>DbContext EF InMemory con nombre único por llamada (aislado entre tests).</summary>
public static class TestDb
{
    public static FacturasProveedorDbContext Create() => Create("facturas-proveedor-" + Guid.NewGuid());

    /// <summary>Contexto sobre una base InMemory con nombre: dos contextos con el mismo nombre comparten datos.</summary>
    public static FacturasProveedorDbContext Create(string name)
    {
        var options = new DbContextOptionsBuilder<FacturasProveedorDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new FacturasProveedorDbContext(options);
    }
}
