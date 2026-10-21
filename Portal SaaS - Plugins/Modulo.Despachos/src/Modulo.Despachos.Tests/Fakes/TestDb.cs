using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;

namespace Modulo.Despachos.Tests.Fakes;

/// <summary>DbContext EF InMemory con nombre único por llamada (aislado entre tests).</summary>
public static class TestDb
{
    public static DespachosDbContext Create() => Create("despachos-" + Guid.NewGuid());

    /// <summary>Contexto sobre una base InMemory con nombre: dos contextos con el mismo nombre comparten datos.</summary>
    public static DespachosDbContext Create(string name)
    {
        var options = new DbContextOptionsBuilder<DespachosDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new DespachosDbContext(options);
    }
}
