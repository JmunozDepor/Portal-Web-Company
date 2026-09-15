using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

public class SupplierHintServiceTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private const string Rut = "76.192.083-9";
    private const string RutNorm = "76192083-9";

    private static RendicionesDbContext CreateDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<RendicionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new RendicionesDbContext(options);
    }

    [Fact]
    public async Task Creates_hint_on_first_confirmed_save_and_normalizes_rut()
    {
        await using var db = CreateDb(nameof(Creates_hint_on_first_confirmed_save_and_normalizes_rut));
        var sut = new SupplierHintService(db);

        await sut.RegisterAsync(Company, Rut, "Comercial Ejemplo SpA", expenseTypeId: 5, documentTypeId: 3);

        var hint = await sut.GetAsync(Company, RutNorm);
        Assert.NotNull(hint);
        Assert.Equal(RutNorm, hint!.SupplierTaxId);
        Assert.Equal("Comercial Ejemplo SpA", hint.SupplierName);
        Assert.Equal(5, hint.DefaultExpenseTypeId);
        Assert.Equal(1, hint.TimesCategoryConfirmed);
        Assert.Equal(3, hint.DefaultDocumentTypeId);
        Assert.Equal(1, hint.TimesSeen);
    }

    [Fact]
    public async Task Default_category_follows_the_mode_not_the_last()
    {
        await using var db = CreateDb(nameof(Default_category_follows_the_mode_not_the_last));
        var sut = new SupplierHintService(db);

        await sut.RegisterAsync(Company, Rut, "P", 5, null); // Almuerzo
        await sut.RegisterAsync(Company, Rut, "P", 5, null); // Almuerzo
        await sut.RegisterAsync(Company, Rut, "P", 9, null); // Otros (una sola vez, la más reciente)

        var hint = await sut.GetAsync(Company, RutNorm);
        Assert.Equal(5, hint!.DefaultExpenseTypeId);      // la moda, no la última
        Assert.Equal(2, hint.TimesCategoryConfirmed);
        Assert.Equal(3, hint.TimesSeen);
    }

    [Fact]
    public async Task Tie_breaks_towards_the_most_recent_category()
    {
        await using var db = CreateDb(nameof(Tie_breaks_towards_the_most_recent_category));
        var sut = new SupplierHintService(db);

        await sut.RegisterAsync(Company, Rut, "P", 5, null);
        await sut.RegisterAsync(Company, Rut, "P", 9, null); // empate 1-1 -> gana la recién sumada (9)

        var hint = await sut.GetAsync(Company, RutNorm);
        Assert.Equal(9, hint!.DefaultExpenseTypeId);
    }

    [Fact]
    public async Task No_op_when_rut_is_invalid()
    {
        await using var db = CreateDb(nameof(No_op_when_rut_is_invalid));
        var sut = new SupplierHintService(db);

        await sut.RegisterAsync(Company, "76.192.083-1", "P", 5, null); // DV incorrecto

        Assert.Empty(db.SupplierHints);
    }
}
