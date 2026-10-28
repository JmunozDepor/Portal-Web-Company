using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Servicios;
using Modulo.FacturasProveedor.Tests.Fakes;

namespace Modulo.FacturasProveedor.Tests.Servicios;

public class SapLookupServiceTests
{
    private const string PoSql = "SELECT * FROM OPOR WHERE \"DocNum\" = :doc_num";
    private const string InvoicedSql = "SELECT * FROM OPCH WHERE \"BaseEntry\" = :doc_entry";
    private const string SupplierSql = "SELECT * FROM OCRD WHERE \"LicTradNum\" = :tax_id";

    private sealed record Env(SapLookupService Svc, FacturasProveedorDbContext Db, FakeHanaService Hana);

    private static async Task<Env> BuildAsync()
    {
        var db = TestDb.Create();
        var hana = new FakeHanaService();
        var svc = new SapLookupService(db, hana, new FakeCurrentCompanyAccessor(), new FakeCurrentUserContext(),
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero)));
        await svc.SaveQueryAsync(SapQueryKinds.PurchaseOrderByDocNum, PoSql, true, CancellationToken.None);
        await svc.SaveQueryAsync(SapQueryKinds.InvoicedByPurchaseOrder, InvoicedSql, true, CancellationToken.None);
        await svc.SaveQueryAsync(SapQueryKinds.SupplierByTaxId, SupplierSql, true, CancellationToken.None);
        return new Env(svc, db, hana);
    }

    private static Dictionary<string, object?> PoRow(int docEntry = 4612, string series = "Primario") => new()
    {
        ["DOC_ENTRY"] = docEntry, ["doc_num"] = 4512, ["series"] = series, ["card_code"] = "P76412553",
        ["supplier_tax_id"] = "76.412.553-1", ["doc_date"] = new DateTime(2026, 10, 12), ["total_local"] = 3200000.0m,
        ["currency"] = "CLP", ["status"] = "OPEN",
    };

    private static object? Param(FakeHanaService hana, string name) =>
        hana.LastParameters!.GetType().GetProperty(name)!.GetValue(hana.LastParameters);

    [Fact]
    public async Task FindPurchaseOrders_PasaDocNum_YMapea()
    {
        var env = await BuildAsync();
        env.Hana.Results[PoSql] = new[] { PoRow() };

        var po = (await env.Svc.FindPurchaseOrdersAsync(4512, CancellationToken.None)).Single();

        Assert.Equal(4512, Param(env.Hana, "doc_num"));
        Assert.Equal(4612, po.DocEntry);
        Assert.Equal("76412553-1", po.SupplierTaxId);
        Assert.Equal(new DateOnly(2026, 10, 12), po.DocDate);
        Assert.Equal(3_200_000m, po.TotalLocal);
        Assert.Equal("open", po.Status);
    }

    [Fact]
    public async Task FindPurchaseOrders_DosSeries_DevuelveAmbas()
    {
        var env = await BuildAsync();
        env.Hana.Results[PoSql] = new[] { PoRow(4612, "Primario"), PoRow(9001, "Importaciones") };

        Assert.Equal(2, (await env.Svc.FindPurchaseOrdersAsync(4512, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task FindInvoiced_ConsultaInactiva_Vacio()
    {
        var env = await BuildAsync();
        env.Hana.Results[InvoicedSql] = new[]
        {
            new Dictionary<string, object?> { ["supplier_tax_id"] = "76412553-1", ["folio"] = 18101L, ["total_amount_local"] = 1000000m, ["invoice_doc_entry"] = 7 },
        };
        Assert.Single(await env.Svc.FindInvoicedAsync(4612, CancellationToken.None));

        await env.Svc.SaveQueryAsync(SapQueryKinds.InvoicedByPurchaseOrder, InvoicedSql, false, CancellationToken.None);

        Assert.Empty(await env.Svc.FindInvoicedAsync(4612, CancellationToken.None));
    }

    [Fact]
    public async Task FindSupplierByTaxId_NormalizaRut()
    {
        var env = await BuildAsync();
        env.Hana.Results[SupplierSql] = new[]
        {
            new Dictionary<string, object?> { ["card_code"] = "P76412553", ["card_name"] = "Comercial Textil Andes SpA", ["tax_id"] = "76412553-1", ["is_active"] = 1 },
        };

        var s = await env.Svc.FindSupplierByTaxIdAsync("76.412.553-k", CancellationToken.None);

        Assert.Equal("76412553-K", Param(env.Hana, "tax_id"));
        Assert.Equal("P76412553", s!.CardCode);
        Assert.True(s.IsActive);
    }

    [Fact]
    public async Task Test_ColumnasFaltantes()
    {
        var env = await BuildAsync();
        var row = PoRow();
        row.Remove("status");
        env.Hana.Results[PoSql] = new[] { row };

        var r = await env.Svc.TestQueryAsync(SapQueryKinds.PurchaseOrderByDocNum, PoSql, "4512", CancellationToken.None);

        Assert.False(r.IsValid);
        Assert.Equal(new[] { "status" }, r.MissingColumns);
    }

    [Theory]
    [InlineData("UPDATE OPOR SET \"Comments\" = 'x' WHERE \"DocNum\" = :doc_num")]
    [InlineData("SELECT 1 FROM OPOR WHERE \"DocNum\" = :doc_num; DELETE FROM OPOR")]
    [InlineData("SELECT 1 FROM OPOR")]
    [InlineData("SELECT 1 FROM OPOR WHERE \"DocNum\" = :otro")]
    public async Task Save_RechazaNoSelect(string sql)
    {
        var env = await BuildAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.SaveQueryAsync(SapQueryKinds.PurchaseOrderByDocNum, sql, true, CancellationToken.None));
    }

    [Fact]
    public async Task List_PrimeraVez_CargaSeeds()
    {
        var db = TestDb.Create();
        var svc = new SapLookupService(db, new FakeHanaService(), new FakeCurrentCompanyAccessor(), new FakeCurrentUserContext(),
            new FixedTimeProvider(DateTimeOffset.UtcNow));

        var list = await svc.ListQueriesAsync(CancellationToken.None);

        Assert.Equal(4, list.Count);
        Assert.All(list, q => Assert.Null(SapSqlGuard.Check(q.SqlText, SapQueryKinds.Parameters(q.Kind))));
        Assert.Contains("OPOR", list.Single(q => q.Kind == SapQueryKinds.PurchaseOrderByDocNum).SqlText);
        Assert.Equal(4, await db.SapQueries.CountAsync());
    }
}
