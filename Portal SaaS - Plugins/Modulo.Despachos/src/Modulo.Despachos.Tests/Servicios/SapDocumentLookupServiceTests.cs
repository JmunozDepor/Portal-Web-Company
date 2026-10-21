using Modulo.Despachos.Data;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;

namespace Modulo.Despachos.Tests.Servicios;

public class SapDocumentLookupServiceTests
{
    private const string InvoiceSql = "SELECT 1 AS \"doc_entry\" FROM DUMMY WHERE :folio = :folio";
    private const string GuideSql = "SELECT 2 AS \"doc_entry\" FROM DUMMY WHERE :folio = :folio";

    private static (SapDocumentLookupService Svc, DespachosDbContext Db, FakeHanaService Hana, FakeCurrentCompanyAccessor Company) Build(
        string engine = "hana")
    {
        var db = TestDb.Create();
        var hana = new FakeHanaService { EngineType = engine };
        var company = new FakeCurrentCompanyAccessor();
        return (new SapDocumentLookupService(db, company, new FakeCurrentUserContext(), hana), db, hana, company);
    }

    private static async Task Configure(DespachosDbContext db, Guid companyId, string kind, string sql, bool active = true)
    {
        db.SapDocumentQueries.Add(new SapDocumentQuery
        {
            CompanyId = companyId, DocumentKind = kind, SqlText = sql, IsActive = active, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static Dictionary<string, object?> Row(bool upper = false, bool omitPackages = false)
    {
        var d = new Dictionary<string, object?>
        {
            ["doc_entry"] = 10, ["doc_num"] = 20, ["folio"] = 1500L, ["sii_document_type"] = 33,
            ["document_date"] = new DateTime(2026, 10, 15), ["customer_code"] = "C1", ["customer_name"] = "Cliente Uno",
            ["customer_tax_id"] = "76.086.428-5", ["destination_address"] = "Av. Siempre Viva 123",
            ["destination_commune"] = "Maipú", ["customer_po"] = "OC-9", ["base_order_entry"] = 77,
            ["units"] = 12.5m, ["packages"] = 3, ["total_amount"] = 119000m, ["is_canceled"] = "N",
            ["weight_kg"] = 40m, ["volume_m3"] = null,
        };
        if (omitPackages) d.Remove("packages");
        return upper ? d.ToDictionary(k => k.Key.ToUpperInvariant(), k => k.Value) : d;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(Dictionary<string, object?> row) =>
        new IReadOnlyDictionary<string, object?>[] { row };

    [Fact]
    public async Task Find_UsaLaConsultaDelTipo_YPasaFolio()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, DocumentKinds.Invoice, InvoiceSql);
        hana.Results[InvoiceSql] = Rows(Row());

        var info = await svc.FindAsync(DocumentKinds.Invoice, 1500, CancellationToken.None);

        Assert.NotNull(info);
        Assert.Equal("Cliente Uno", info!.CustomerName);
        Assert.Equal(DocumentKinds.Invoice, info.DocumentKind);
        Assert.Equal(33, info.SiiDocumentType);
        Assert.Equal(new DateOnly(2026, 10, 15), info.DocumentDate);
        Assert.Equal(12.5m, info.Units);
        Assert.Equal(3, info.Packages);
        Assert.Equal(77, info.BaseOrderEntry);
        Assert.False(info.IsCanceled);
        Assert.Equal(40m, info.WeightKg);
        Assert.Null(info.VolumeM3);
        var p = hana.LastParameters!;
        Assert.Equal(1500L, Convert.ToInt64(p.GetType().GetProperty("folio")!.GetValue(p)));
    }

    [Fact]
    public async Task Find_MismoFolioOtroTipo_NoMezcla()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, DocumentKinds.TransferGuide, GuideSql);
        await Configure(db, company.CompanyId, DocumentKinds.Invoice, InvoiceSql);
        hana.Results[GuideSql] = Rows(Row());   // folio 1500 existe solo como guía

        Assert.Null(await svc.FindAsync(DocumentKinds.Invoice, 1500, CancellationToken.None));
        Assert.NotNull(await svc.FindAsync(DocumentKinds.TransferGuide, 1500, CancellationToken.None));
    }

    [Fact]
    public async Task Find_SinConsultaConfigurada_LanzaMensajeClaro()
    {
        var (svc, _, _, _) = Build(engine: "PostgreSQL");   // sin HANA no se siembra
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.FindAsync(DocumentKinds.Invoice, 1500, CancellationToken.None));
        Assert.Contains("No hay consulta SAP configurada para facturas", ex.Message);
    }

    [Fact]
    public async Task Find_ConsultaInactiva_SeTrataComoNoConfigurada()
    {
        var (svc, db, _, company) = Build();
        await Configure(db, company.CompanyId, DocumentKinds.Invoice, InvoiceSql, active: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.FindAsync(DocumentKinds.Invoice, 1500, CancellationToken.None));
    }

    [Fact]
    public async Task Find_SinFilaEnHana_SiembraElSeed_YEjecutaEseSql()
    {
        var (svc, db, hana, company) = Build(engine: "SAP HANA");

        var info = await svc.FindAsync(DocumentKinds.TransferGuide, 99, CancellationToken.None);

        Assert.Null(info);   // el fake no devuelve filas
        var row = db.SapDocumentQueries.Single(q => q.CompanyId == company.CompanyId);
        Assert.Equal(DocumentKinds.TransferGuide, row.DocumentKind);
        Assert.Contains("@INX_ODGC", row.SqlText);
        Assert.Single(hana.ExecutedSql);
        Assert.Contains("@INX_ODGC", hana.ExecutedSql[0]);
    }

    [Fact]
    public async Task Find_SemillaSoloElTipoPedido_YNoDuplicaEnLlamadasSiguientes()
    {
        var (svc, db, _, _) = Build();
        await svc.FindAsync(DocumentKinds.Invoice, 1, CancellationToken.None);
        await svc.FindAsync(DocumentKinds.Invoice, 2, CancellationToken.None);
        Assert.Equal(DocumentKinds.Invoice, db.SapDocumentQueries.Single().DocumentKind);
    }

    [Fact]
    public async Task List_EnHana_SiembraAmbosTipos_YEnOtroMotorNo()
    {
        var (svc, db, _, _) = Build();
        var list = await svc.ListQueriesAsync();
        Assert.Equal(2, list.Count);
        Assert.Equal(2, db.SapDocumentQueries.Count());

        var (svc2, db2, _, _) = Build(engine: "PostgreSQL");
        Assert.Empty(await svc2.ListQueriesAsync());
        Assert.Empty(db2.SapDocumentQueries);
    }

    [Fact]
    public async Task Find_ConsultaDeOtraCompania_NoSeUsa()
    {
        var (svc, db, hana, _) = Build(engine: "PostgreSQL");
        await Configure(db, Guid.NewGuid(), DocumentKinds.Invoice, InvoiceSql);
        hana.Results[InvoiceSql] = Rows(Row());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.FindAsync(DocumentKinds.Invoice, 1500, CancellationToken.None));
    }

    [Fact]
    public async Task Find_ClavesEnMayusculas_SeLeenIgual()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, DocumentKinds.Invoice, InvoiceSql);
        hana.Results[InvoiceSql] = Rows(Row(upper: true));

        var info = await svc.FindAsync(DocumentKinds.Invoice, 1500, CancellationToken.None);
        Assert.Equal(10, info!.DocEntry);
        Assert.Equal("Cliente Uno", info.CustomerName);
    }

    [Fact]
    public async Task Find_ColumnaObligatoriaFaltante_LanzaNombrandoLaColumna()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, DocumentKinds.Invoice, InvoiceSql);
        hana.Results[InvoiceSql] = Rows(Row(omitPackages: true));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.FindAsync(DocumentKinds.Invoice, 1500, CancellationToken.None));
        Assert.Contains("packages", ex.Message);
    }

    [Fact]
    public async Task Validate_ReportaColumnasFaltantes()
    {
        var (svc, _, hana, _) = Build();
        hana.Results[InvoiceSql] = Rows(Row(omitPackages: true));

        var result = await svc.ValidateQueryAsync(InvoiceSql, 1500);

        Assert.False(result.IsValid);
        Assert.Equal(new[] { "packages" }, result.MissingColumns);
    }

    [Fact]
    public async Task Validate_ConsultaCorrecta_EsValida_ConPuntoYComaFinalYComentarios()
    {
        var (svc, _, hana, _) = Build();
        var sql = "-- busca por folio\n/* bloque */ WITH x AS (SELECT 1 FROM DUMMY) SELECT 1 AS \"doc_entry\" FROM x WHERE :folio = 1;";
        hana.Results[sql.TrimEnd(';')] = Rows(Row());

        var result = await svc.ValidateQueryAsync(sql, 1500);

        Assert.True(result.IsValid);
        Assert.Empty(result.MissingColumns);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("UPDATE oinv SET x = 1 WHERE :folio = 1")]
    [InlineData("delete from oinv where :folio = 1")]
    [InlineData("SELECT 1 FROM DUMMY WHERE :folio = 1; DELETE FROM OINV")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1 FROM DUMMY WHERE x = :otro")]
    [InlineData("")]
    public async Task Validate_RechazaSentenciasNoSelect_SinEjecutar(string sql)
    {
        var (svc, _, hana, _) = Build();

        var result = await svc.ValidateQueryAsync(sql, 1500);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Empty(hana.ExecutedSql);
    }

    [Fact]
    public async Task Validate_PuntoYComaDentroDeLiteralOComentario_NoCuenta()
    {
        var (svc, _, hana, _) = Build();
        var sql = "SELECT 'a;b' AS \"doc_entry\" FROM DUMMY WHERE :folio = 1 -- fin; ok";
        hana.Results[sql] = Rows(Row());
        Assert.True((await svc.ValidateQueryAsync(sql, 1)).IsValid);
    }

    [Fact]
    public async Task Validate_SinFilasParaElFolio_NoEsValida()
    {
        var (svc, _, _, _) = Build();
        var result = await svc.ValidateQueryAsync(InvoiceSql, 1500);
        Assert.False(result.IsValid);
        Assert.Contains("folio", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Validate_ErrorDeSap_SeReportaSinLanzar()
    {
        var (svc, _, hana, _) = Build();
        hana.ThrowOnQuery = new InvalidOperationException("invalid table name: OINVX");
        var result = await svc.ValidateQueryAsync(InvoiceSql, 1500);
        Assert.False(result.IsValid);
        Assert.Contains("OINVX", result.Error);
    }

    [Fact]
    public async Task SaveQuery_ValidaYMarcaValidatedAt_SinoRechazaYNoGuarda()
    {
        var (svc, db, hana, _) = Build();
        hana.Results[InvoiceSql] = Rows(Row());

        await svc.SaveQueryAsync(DocumentKinds.Invoice, InvoiceSql, true, 1500);
        var saved = db.SapDocumentQueries.Single();
        Assert.NotNull(saved.ValidatedAt);
        Assert.Equal(DateTimeKind.Utc, saved.ValidatedAt!.Value.Kind);

        // Consulta nueva inválida -> no cambia la guardada.
        var bad = "SELECT 1 FROM DUMMY WHERE :folio = 1";
        hana.Results[bad] = Rows(Row(omitPackages: true));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SaveQueryAsync(DocumentKinds.Invoice, bad, true, 1500));
        Assert.Equal(InvoiceSql, db.SapDocumentQueries.Single().SqlText);
    }

    [Fact]
    public async Task SaveQuery_TipoInvalido_Lanza()
    {
        var (svc, _, _, _) = Build();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SaveQueryAsync("otro", InvoiceSql, true, 1));
    }

    [Theory]
    [InlineData(33, "invoice")]
    [InlineData(34, "invoice")]
    [InlineData(52, "transfer_guide")]
    public void DocumentKindFor_33_34_52(int sii, string expected) =>
        Assert.Equal(expected, SapDocumentLookupService.DocumentKindFor(sii));

    [Fact]
    public void DocumentKindFor_TipoDesconocido_Lanza() =>
        Assert.Throws<InvalidOperationException>(() => SapDocumentLookupService.DocumentKindFor(61));

    [Fact]
    public void Seeds_Hana_ContienenElContratoCompleto_SinEsquemaFijo()
    {
        foreach (var kind in DocumentKinds.All)
        {
            var sql = SapQuerySeeds.Get(kind);
            foreach (var col in SapDocumentLookupService.RequiredColumns)
                Assert.Contains($"\"{col}\"", sql);
            Assert.DoesNotContain("CLPRDDEPOR", sql);
        }
    }

    [Theory]
    [InlineData("hana", true)]
    [InlineData("SAP HANA", true)]
    [InlineData("SqlServer", false)]
    [InlineData("PostgreSQL", false)]
    [InlineData("", false)]
    public void IsHana_DetectaElMotor(string engine, bool expected) =>
        Assert.Equal(expected, SapDocumentLookupService.IsHana(engine));
}
