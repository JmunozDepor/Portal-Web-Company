using Modulo.Despachos.Data;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Consultas SAP de transferencias (Fase B): por LPN, pendientes por bodega, líneas y bodegas.</summary>
public class SapTransferQueriesTests
{
    private const string LpnSql = "SELECT 1 AS \"doc_entry\" FROM DUMMY WHERE :lpn = :lpn";
    private const string PendingSql = "SELECT 1 AS \"doc_entry\" FROM DUMMY WHERE :warehouse = :warehouse AND :from_date IS NOT NULL";
    private const string LinesSql = "SELECT 1 AS \"line_num\" FROM DUMMY WHERE :doc_entry = :doc_entry";
    private const string WarehousesSql = "SELECT 'TDA01' AS \"warehouse_code\" FROM DUMMY";

    private static (SapDocumentLookupService Svc, DespachosDbContext Db, FakeHanaService Hana, FakeCurrentCompanyAccessor Company) Build(
        string engine = "hana")
    {
        var db = TestDb.Create();
        var hana = new FakeHanaService { EngineType = engine };
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

    private static Dictionary<string, object?> TransferRow(int docEntry = 501, string lpn = "CTBO123", string canceled = "N") => new()
    {
        ["doc_entry"] = docEntry, ["doc_num"] = docEntry + 1000, ["document_date"] = new DateTime(2026, 10, 20),
        ["warehouse_code"] = "TDA01", ["warehouse_name"] = "Tienda Estado", ["destination_address"] = "Estado 387",
        ["destination_commune"] = "Santiago", ["units"] = 24m, ["is_canceled"] = canceled, ["lpn"] = lpn,
    };

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(params Dictionary<string, object?>[] rows) => rows;

    private static object? Param(FakeHanaService hana, string name) =>
        hana.LastParameters!.GetType().GetProperty(name)!.GetValue(hana.LastParameters);

    [Fact]
    public async Task FindTransferByLpn_MapeaContrato_YEnlazaLpn()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.TransferByLpn, LpnSql);
        hana.Results[LpnSql] = Rows(TransferRow());

        var t = await svc.FindTransferByLpnAsync("CTBO123");

        Assert.NotNull(t);
        Assert.Equal((501, 1501, "TDA01", 24m, false, "CTBO123"), (t!.DocEntry, t.DocNum, t.WarehouseCode, t.Units, t.IsCanceled, t.Lpn));
        Assert.Equal(new DateOnly(2026, 10, 20), t.DocumentDate);
        Assert.Equal("CTBO123", Param(hana, "lpn"));
    }

    [Fact]
    public async Task FindTransferByLpn_SinFila_Null()
    {
        var (svc, db, _, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.TransferByLpn, LpnSql);
        Assert.Null(await svc.FindTransferByLpnAsync("CTBO999"));
    }

    [Fact]
    public async Task FindTransferByLpn_DosFilas_Lanza()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.TransferByLpn, LpnSql);
        hana.Results[LpnSql] = Rows(TransferRow(501), TransferRow(502));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.FindTransferByLpnAsync("CTBO123"));
        Assert.Contains("más de una transferencia", ex.Message);
    }

    [Fact]
    public async Task FindTransferByLpn_AnuladaYRecreada_DevuelveLaVigente()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.TransferByLpn, LpnSql);
        hana.Results[LpnSql] = Rows(TransferRow(501, canceled: "Y"), TransferRow(503));

        var t = await svc.FindTransferByLpnAsync("CTBO123");

        Assert.Equal(503, t!.DocEntry);
    }

    [Fact]
    public async Task Seed_TransferenciaPorLpn_ExcluyeDocumentosDeCancelacion()
    {
        var (svc, _, _, _) = Build();
        var sql = (await svc.ListQueriesAsync()).Single(q => q.DocumentKind == SapQueryKinds.TransferByLpn).SqlText;
        Assert.Contains("\"CANCELED\" <> 'C'", sql);
    }

    [Fact]
    public async Task ListPendingTransfers_EnlazaBodegaYFecha()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.TransferPending, PendingSql);
        hana.Results[PendingSql] = Rows(TransferRow(501), TransferRow(502, "CTBO124"));

        var list = await svc.ListPendingTransfersAsync("TDA01", new DateOnly(2026, 10, 1));

        Assert.Equal(new[] { 501, 502 }, list.Select(x => x.DocEntry));
        Assert.Equal("TDA01", Param(hana, "warehouse"));
        Assert.Equal(new DateTime(2026, 10, 1), Param(hana, "from_date"));
    }

    [Fact]
    public async Task GetLines_Transferencia_UsaConsultaDeLineasDeTransferencia()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.TransferLines, LinesSql);
        hana.Results[LinesSql] = Rows(new Dictionary<string, object?>
        {
            ["line_num"] = 0, ["item_code"] = "A1", ["item_name"] = "Zapatilla", ["quantity"] = 3m, ["unit"] = "UN",
            ["weight_kg"] = null, ["customer_activity"] = null, ["destination_city"] = "Santiago",
        });

        var lines = await svc.GetLinesAsync(SapQueryKinds.TransferLines, 501);

        Assert.Equal("A1", Assert.Single(lines.Lines).ItemCode);
        Assert.Equal(501, Param(hana, "doc_entry"));
    }

    [Fact]
    public async Task ListSapWarehouses_Mapea()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.Warehouses, WarehousesSql);
        hana.Results[WarehousesSql] = Rows(new Dictionary<string, object?>
        {
            ["warehouse_code"] = "TDA01", ["warehouse_name"] = "Tienda Estado", ["address"] = "Estado 387", ["commune"] = "Santiago",
        });

        var w = Assert.Single(await svc.ListSapWarehousesAsync());
        Assert.Equal(new SapWarehouse("TDA01", "Tienda Estado", "Estado 387", "Santiago"), w);
    }

    [Fact]
    public async Task Seeds_SinFilaEnHana_SiembraLasCuatroConsultasDeTransferencias()
    {
        var (svc, _, _, _) = Build();

        var queries = await svc.ListQueriesAsync();

        Assert.Equal(7, queries.Count);
        Assert.Contains("\"U_NX_LPN\" = :lpn", queries.Single(q => q.DocumentKind == SapQueryKinds.TransferByLpn).SqlText);
        Assert.Contains(":warehouse", queries.Single(q => q.DocumentKind == SapQueryKinds.TransferPending).SqlText);
        Assert.Contains("WTR1", queries.Single(q => q.DocumentKind == SapQueryKinds.TransferLines).SqlText);
        Assert.Contains("OWHS", queries.Single(q => q.DocumentKind == SapQueryKinds.Warehouses).SqlText);
        Assert.All(queries, q => Assert.DoesNotContain("@INX_DGC1", q.DocumentKind == DocumentKinds.TransferGuide ? "" : q.SqlText));
    }

    [Fact]
    public async Task Probe_TransferenciaPorLpn_ConMuestraTexto()
    {
        var (svc, _, hana, _) = Build();
        hana.Results[LpnSql] = Rows(TransferRow());

        var r = await svc.ProbeAsync(SapQueryKinds.TransferByLpn, LpnSql, 0, "CTBO123");

        Assert.True(r.Validation.IsValid, r.Validation.Error);
        Assert.Equal(501, Assert.Single(r.Transfers!).DocEntry);
        Assert.Equal("CTBO123", Param(hana, "lpn"));
    }

    [Fact]
    public async Task Probe_TransferenciaPorLpn_SinParametroLpn_Invalida()
    {
        var (svc, _, _, _) = Build();
        var r = await svc.ProbeAsync(SapQueryKinds.TransferByLpn, "SELECT 1 FROM DUMMY WHERE :folio = 1", 0, "CTBO123");
        Assert.False(r.Validation.IsValid);
    }

    [Fact]
    public async Task Probe_Bodegas_SinParametros()
    {
        var (svc, _, hana, _) = Build();
        hana.Results[WarehousesSql] = Rows(new Dictionary<string, object?>
        {
            ["warehouse_code"] = "TDA01", ["warehouse_name"] = "Tienda", ["address"] = null, ["commune"] = null,
        });

        var r = await svc.ProbeAsync(SapQueryKinds.Warehouses, WarehousesSql, 0);

        Assert.True(r.Validation.IsValid, r.Validation.Error);
        Assert.Equal("TDA01", Assert.Single(r.Warehouses!).Code);
    }
}
