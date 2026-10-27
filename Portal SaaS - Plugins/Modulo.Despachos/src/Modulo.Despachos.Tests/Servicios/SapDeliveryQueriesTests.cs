using Modulo.Despachos.Data;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Consultas SAP de Entregas con guía previa: pendientes por cliente, por LPN y líneas.</summary>
public class SapDeliveryQueriesTests
{
    private const string PendingSql = "SELECT 1 AS \"doc_entry\" FROM DUMMY WHERE :customer = :customer AND :from_date IS NOT NULL";
    private const string LpnSql = "SELECT 1 AS \"doc_entry\" FROM DUMMY WHERE :lpn = :lpn";
    private const string LinesSql = "SELECT 1 AS \"line_num\" FROM DUMMY WHERE :doc_entry = :doc_entry";

    private static (SapDocumentLookupService Svc, DespachosDbContext Db, FakeHanaService Hana, FakeCurrentCompanyAccessor Company) Build()
    {
        var db = TestDb.Create();
        var hana = new FakeHanaService { EngineType = "hana" };
        var company = new FakeCurrentCompanyAccessor();
        return (new SapDocumentLookupService(db, company, new FakeCurrentUserContext(), hana), db, hana, company);
    }

    private static async Task Configure(DespachosDbContext db, Guid companyId, string kind, string sql)
    {
        db.SapDocumentQueries.Add(new SapDocumentQuery
        {
            CompanyId = companyId, DocumentKind = kind, SqlText = sql, IsActive = true, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    internal static Dictionary<string, object?> DeliveryRow(int docEntry = 701, string lpn = "LPN701", string? po = "4500123456",
        int poCount = 1, int open = 1) => new()
    {
        ["doc_entry"] = docEntry, ["doc_num"] = docEntry + 1000, ["document_date"] = new DateTime(2026, 10, 24),
        ["customer_code"] = "C001", ["customer_name"] = "Falabella", ["customer_tax_id"] = "77261280-K",
        ["destination_address"] = "Av. Presidente Kennedy 9001", ["destination_commune"] = "Las Condes",
        ["lpn"] = lpn, ["customer_po"] = po, ["order_date"] = new DateTime(2026, 10, 20), ["po_count"] = poCount,
        ["units"] = 12m, ["net_amount"] = 120000m, ["is_open"] = open,
    };

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(params Dictionary<string, object?>[] rows) => rows;

    private static object? Param(FakeHanaService hana, string name) =>
        hana.LastParameters!.GetType().GetProperty(name)!.GetValue(hana.LastParameters);

    [Fact]
    public async Task Pendientes_MapeaContrato_YEnlazaClienteYFecha()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.DeliveryPending, PendingSql);
        hana.Results[PendingSql] = Rows(DeliveryRow());

        var d = Assert.Single(await svc.ListPendingDeliveriesAsync("C001", new DateOnly(2026, 10, 1)));

        Assert.Equal((701, 1701, "C001", "Falabella", "77261280-K", "LPN701", "4500123456", 1, 12m, 120000m, true),
            (d.DocEntry, d.DocNum, d.CustomerCode, d.CustomerName, d.CustomerTaxId, d.Lpn, d.CustomerPo, d.PoCount, d.Units, d.NetAmount, d.IsOpen));
        Assert.Equal((new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 20)), (d.DocumentDate, d.OrderDate!.Value));
        Assert.Equal(("Av. Presidente Kennedy 9001", "Las Condes"), (d.DestinationAddress, d.DestinationCommune));
        Assert.Equal("C001", Param(hana, "customer"));
        Assert.Equal(new DateTime(2026, 10, 1), Param(hana, "from_date"));
    }

    [Fact]
    public async Task PorLpn_DosEntregas_PrefiereLaAbierta()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.DeliveryByLpn, LpnSql);
        hana.Results[LpnSql] = Rows(DeliveryRow(701, open: 0), DeliveryRow(702));

        var d = await svc.FindDeliveryByLpnAsync("LPN701");

        Assert.Equal(702, d!.DocEntry);
        Assert.Equal("LPN701", Param(hana, "lpn"));
    }

    [Fact]
    public async Task PorLpn_DosAbiertas_Lanza()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.DeliveryByLpn, LpnSql);
        hana.Results[LpnSql] = Rows(DeliveryRow(701), DeliveryRow(702));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.FindDeliveryByLpnAsync("LPN701"));
        Assert.Equal("El LPN LPN701 está en más de una Entrega en SAP; revise el LPN.", ex.Message);
    }

    [Fact]
    public async Task PorLpn_SinFila_Null()
    {
        var (svc, db, _, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.DeliveryByLpn, LpnSql);
        Assert.Null(await svc.FindDeliveryByLpnAsync("NOEXISTE"));
    }

    [Fact]
    public async Task Lineas_DeliveryLines_UsaContratoDeLineas()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.DeliveryLines, LinesSql);
        hana.Results[LinesSql] = Rows(new Dictionary<string, object?>
        {
            ["line_num"] = 0, ["item_code"] = "SKU1", ["item_name"] = "Zapatilla", ["quantity"] = 3m, ["unit"] = "UN",
            ["weight_kg"] = null, ["customer_activity"] = "Grandes tiendas", ["destination_city"] = "Santiago", ["line_amount"] = 30000m,
        });

        var l = await svc.GetLinesAsync(SapQueryKinds.DeliveryLines, 701);

        Assert.Equal(("Grandes tiendas", "SKU1", 3m, 30000m), (l.CustomerActivity, l.Lines[0].ItemCode, l.Lines[0].Quantity, l.Lines[0].Amount!.Value));
        Assert.Equal(701, Param(hana, "doc_entry"));
    }

    [Theory]
    [InlineData(SapQueryKinds.DeliveryPending, ":customer", "ODLN")]
    [InlineData(SapQueryKinds.DeliveryByLpn, ":lpn", "ODLN")]
    [InlineData(SapQueryKinds.DeliveryLines, ":doc_entry", "DLN1")]
    public void Seeds_Entregas_ConSuParametro_YSinParametrosEnComentarios(string kind, string parameter, string table)
    {
        var seed = SapQuerySeeds.Get(kind);
        Assert.Contains(parameter, seed);
        Assert.Contains(table, seed);
        Assert.DoesNotContain(seed.Split('\n'), l => l.TrimStart().StartsWith("--") && l.Contains(':'));
    }

    [Fact]
    public async Task Probar_Pendientes_DevuelveEntregas()
    {
        var (svc, _, hana, _) = Build();
        hana.Results[PendingSql] = Rows(DeliveryRow());

        var r = await svc.ProbeAsync(SapQueryKinds.DeliveryPending, PendingSql, 0, "C001");

        Assert.True(r.Validation.IsValid, r.Validation.Error);
        Assert.Equal(701, Assert.Single(r.Deliveries!).DocEntry);
        Assert.Equal("C001", Param(hana, "customer"));
    }

    [Fact]
    public async Task Probar_PendientesSinCliente_Invalida()
    {
        var (svc, _, _, _) = Build();
        var r = await svc.ProbeAsync(SapQueryKinds.DeliveryPending, "SELECT 1 FROM DUMMY WHERE :lpn = 1", 0, "C001");
        Assert.False(r.Validation.IsValid);
    }
}
