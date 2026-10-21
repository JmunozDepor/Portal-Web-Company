using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;

namespace Modulo.Despachos.Tests.Fakes;

/// <summary>DbContext EF InMemory con nombre único por llamada (aislado entre tests).</summary>
public static class TestDb
{
    public static DespachosDbContext Create()
    {
        var options = new DbContextOptionsBuilder<DespachosDbContext>()
            .UseInMemoryDatabase("despachos-" + Guid.NewGuid())
            .Options;
        return new DespachosDbContext(options);
    }
}
