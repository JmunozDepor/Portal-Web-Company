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

    private static Dictionary<string, object?> Row(bool upper = false, bool omitPackages = false, int sii = 33, int docEntry = 10)
    {
        var d = new Dictionary<string, object?>
        {
            ["doc_entry"] = docEntry, ["doc_num"] = 20, ["folio"] = 1500L, ["sii_document_type"] = sii,
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

        var info = await svc.FindAsync(DocumentKinds.Invoice, 1500, ct: CancellationToken.None);

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

    private static object Param(object p, string name) => p.GetType().GetProperty(name)!.GetValue(p)!;

    [Fact]
    public async Task Find_PasaSiiDocumentTypeCeroPorDefecto()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, DocumentKinds.Invoice, InvoiceSql);
        hana.Results[InvoiceSql] = Rows(Row());

        await svc.FindAsync(DocumentKinds.Invoice, 1500, ct: CancellationToken.None);
        Assert.Equal(0, Convert.ToInt32(Param(hana.LastParameters!, "sii_document_type")));

        await svc.FindAsync(DocumentKinds.Invoice, 1500, 34);
        Assert.Equal(34, Convert.ToInt32(Param(hana.LastParameters!, "sii_document_type")));
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Duplicado33y34() =>
        new IReadOnlyDictionary<string, object?>[] { Row(sii: 33, docEntry: 10), Row(sii: 34, docEntry: 11) };

    [Fact]
    public async Task Find_FolioDuplicado33y34_SinTipo_LanzaAmbiguo()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, DocumentKinds.Invoice, InvoiceSql);
        hana.Results[InvoiceSql] = Duplicado33y34();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.FindAsync(DocumentKinds.Invoice, 1500, ct: CancellationToken.None));
        Assert.Contains("más de un tipo de documento (33, 34)", ex.Message);
        Assert.Contains("1500", ex.Message);
    }

    [Fact]
    public async Task Find_FolioDuplicado_ConTipo34_DevuelveExenta()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, DocumentKinds.Invoice, InvoiceSql);
        hana.Results[InvoiceSql] = Duplicado33y34();

        var info = await svc.FindAsync(DocumentKinds.Invoice, 1500, 34);

        Assert.Equal(34, info!.SiiDocumentType);
        Assert.Equal(11, info.DocEntry);
    }

    [Fact]
    public async Task Find_TipoNoCorrespondeAlKind_Lanza()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, DocumentKinds.Invoice, InvoiceSql);
        hana.Results[InvoiceSql] = Rows(Row(sii: 52));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.FindAsync(DocumentKinds.Invoice, 1500, ct: CancellationToken.None));
        Assert.Contains("no corresponde", ex.Message);
    }

    [Fact]
    public async Task Validate_PermiteSiiDocumentType()
    {
        var (svc, _, hana, _) = Build();
        var sql = "SELECT 1 AS \"doc_entry\" FROM DUMMY WHERE X = :folio AND (:sii_document_type = 0 OR Y = :sii_document_type)";
        hana.Results[sql] = Rows(Row());
        Assert.True((await svc.ValidateQueryAsync(sql, 1500)).IsValid);
        Assert.Equal(0, Convert.ToInt32(Param(hana.LastParameters!, "sii_document_type")));
    }

    [Theory]
    [InlineData("WITH c AS (SELECT 1 FROM DUMMY) DELETE FROM OINV WHERE :folio = 1")]
    [InlineData("WITH c AS (SELECT 1 FROM DUMMY) UPDATE OINV SET x = 1 WHERE :folio = 1")]
    [InlineData("SELECT [x'] FROM DUMMY WHERE :folio = 1; DELETE FROM OINV -- ']")]
    [InlineData("SELECT * INTO t FROM OINV WHERE :folio = 1")]
    [InlineData("SELECT 1 FROM DUMMY WHERE :folio = 1 EXEC sp_who")]
    [InlineData("SELECT 1 FROM DUMMY WHERE :folio = 1 UNION SELECT * FROM OPENROWSET('a','b','c')")]
    [InlineData("SELECT 1 FROM DUMMY WHERE :folio = 1 AND xp_cmdshell('dir') = 1")]
    [InlineData("SELECT 1 FROM DUMMY WHERE :folio = 1 REPLACE INTO t VALUES (1)")]
    [InlineData("SELECT 1 FROM DUMMY WHERE :folio = 1 DROP TABLE t")]
    [InlineData("SELECT 1 FROM DUMMY WHERE :folio = 1 CALL p()")]
    public async Task Validate_RechazaEscrituraYEjecucion_SinEjecutar(string sql)
    {
        var (svc, _, hana, _) = Build();
        var result = await svc.ValidateQueryAsync(sql, 1500);
        Assert.False(result.IsValid);
        Assert.Empty(hana.ExecutedSql);
    }

    [Fact]
    public async Task Validate_ReplaceFuncionYLiteralDelete_SiPasan()
    {
        var (svc, _, hana, _) = Build();
        var sql = "SELECT REPLACE(T0.\"Street\", 'DELETE', '') AS \"doc_entry\", \"UpdateDate\", [Insert] FROM DUMMY T0 WHERE T0.X = :folio AND T0.update_date IS NOT NULL";
        hana.Results[sql] = Rows(Row());
        Assert.True((await svc.ValidateQueryAsync(sql, 1)).IsValid);
    }

    [Fact]
    public async Task Validate_LosSeedsPasanLaValidacionEstatica()
    {
        foreach (var kind in DocumentKinds.All)
        {
            var (svc, _, hana, _) = Build();
            var sql = SapQuerySeeds.Get(kind);
            hana.Results[sql] = Rows(Row(sii: kind == DocumentKinds.Invoice ? 33 : 52));
            var result = await svc.ValidateQueryAsync(sql, 1);
            Assert.True(result.IsValid, kind + ": " + result.Error);
        }
    }

    [Fact]
    public async Task Find_MismoFolioOtroTipo_NoMezcla()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, DocumentKinds.TransferGuide, GuideSql);
        await Configure(db, company.CompanyId, DocumentKinds.Invoice, InvoiceSql);
        hana.Results[GuideSql] = Rows(Row(sii: 52));   // folio 1500 existe solo como guía

        Assert.Null(await svc.FindAsync(DocumentKinds.Invoice, 1500, ct: CancellationToken.None));
        Assert.NotNull(await svc.FindAsync(DocumentKinds.TransferGuide, 1500, ct: CancellationToken.None));
    }

    [Fact]
    public async Task Find_SinConsultaConfigurada_LanzaMensajeClaro()
    {
        var (svc, _, _, _) = Build(engine: "PostgreSQL");   // sin HANA no se siembra
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.FindAsync(DocumentKinds.Invoice, 1500, ct: CancellationToken.None));
        Assert.Contains("No hay consulta SAP configurada para facturas", ex.Message);
    }

    [Fact]
    public async Task Find_ConsultaInactiva_SeTrataComoNoConfigurada()
    {
        var (svc, db, _, company) = Build();
        await Configure(db, company.CompanyId, DocumentKinds.Invoice, InvoiceSql, active: false);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.FindAsync(DocumentKinds.Invoice, 1500, ct: CancellationToken.None));
    }

    [Fact]
    public async Task Find_SinFilaEnHana_SiembraElSeed_YEjecutaEseSql()
    {
        var (svc, db, hana, company) = Build(engine: "SAP HANA");

        var info = await svc.FindAsync(DocumentKinds.TransferGuide, 99, ct: CancellationToken.None);

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
        await svc.FindAsync(DocumentKinds.Invoice, 1, ct: CancellationToken.None);
        await svc.FindAsync(DocumentKinds.Invoice, 2, ct: CancellationToken.None);
        Assert.Equal(DocumentKinds.Invoice, db.SapDocumentQueries.Single().DocumentKind);
    }

    [Fact]
    public async Task List_EnHana_SiembraLasTresConsultas_YEnOtroMotorNo()
    {
        var (svc, db, _, _) = Build();
        var list = await svc.ListQueriesAsync();
        Assert.Equal(3, list.Count);
        Assert.Equal(3, db.SapDocumentQueries.Count());

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
            () => svc.FindAsync(DocumentKinds.Invoice, 1500, ct: CancellationToken.None));
    }

    [Fact]
    public async Task Find_ClavesEnMayusculas_SeLeenIgual()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, DocumentKinds.Invoice, InvoiceSql);
        hana.Results[InvoiceSql] = Rows(Row(upper: true));

        var info = await svc.FindAsync(DocumentKinds.Invoice, 1500, ct: CancellationToken.None);
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
            () => svc.FindAsync(DocumentKinds.Invoice, 1500, ct: CancellationToken.None));
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

    [Fact]
    public void Seed_Factura_LosJoinsInv1ADln1FiltranBaseType15()
    {
        // INV1.BaseEntry/BaseLine apuntan a una entrega solo si BaseType = 15 (si no, pueden ser un pedido u otro documento).
        var sql = SapQuerySeeds.Get(DocumentKinds.Invoice);
        var joins = System.Text.RegularExpressions.Regex.Matches(sql, @"INNER JOIN DLN1 \w+ ON .*");
        Assert.Equal(2, joins.Count);
        foreach (System.Text.RegularExpressions.Match j in joins)
            Assert.Matches(@"\w+\.""BaseType"" = 15", j.Value);
    }

    [Theory]
    [InlineData("hana", true)]
    [InlineData("SAP HANA", true)]
    [InlineData("SqlServer", false)]
    [InlineData("PostgreSQL", false)]
    [InlineData("", false)]
    public void IsHana_DetectaElMotor(string engine, bool expected) =>
        Assert.Equal(expected, SapDocumentLookupService.IsHana(engine));
    // ------------------------------------------------------------------ Líneas de factura (guías Res. 154)

    private const string LinesSql = "SELECT 1 AS \"line_num\" FROM DUMMY WHERE :doc_entry = :doc_entry";

    private static Dictionary<string, object?> LineRow(int line, string item, decimal qty, decimal? weight, bool omitWeight = false)
    {
        var d = new Dictionary<string, object?>
        {
            ["line_num"] = line, ["item_code"] = item, ["item_name"] = $"Artículo {item}", ["quantity"] = qty,
            ["unit"] = "UN", ["weight_kg"] = weight, ["customer_activity"] = "Venta al por menor", ["destination_city"] = "Talca",
        };
        if (omitWeight) d.Remove("weight_kg");
        return d;
    }

    [Fact]
    public async Task ValidateQuery_LineasSinDocEntry_Error()
    {
        var (svc, _, _, _) = Build();
        var r = await svc.ProbeAsync(SapQueryKinds.InvoiceLines, "SELECT 1 FROM DUMMY WHERE :folio = 1", 10);
        Assert.False(r.Validation.IsValid);
        Assert.Contains(":doc_entry", r.Validation.Error);
    }

    [Fact]
    public async Task ValidateQuery_LineasFaltaColumna_ReportaFaltantes()
    {
        var (svc, _, hana, _) = Build();
        hana.Results[LinesSql] = Rows(LineRow(0, "A1", 2, 1.5m, omitWeight: true));
        var r = await svc.ProbeAsync(SapQueryKinds.InvoiceLines, LinesSql, 10);
        Assert.False(r.Validation.IsValid);
        Assert.Equal(new[] { "weight_kg" }, r.Validation.MissingColumns);
    }

    [Fact]
    public async Task ValidateQuery_LineasOk_DevuelveLineas()
    {
        var (svc, _, hana, _) = Build();
        hana.Results[LinesSql] = Rows(LineRow(0, "A1", 2, 1.5m));
        var r = await svc.ProbeAsync(SapQueryKinds.InvoiceLines, LinesSql, 10);
        Assert.True(r.Validation.IsValid);
        Assert.Equal("A1", Assert.Single(r.Lines!.Lines).ItemCode);
    }

    [Fact]
    public async Task GetLines_SinFilaEnHana_SiembraSeedConGirosDeDepor()
    {
        var (svc, db, hana, company) = Build(engine: "SAP HANA");

        await svc.GetLinesAsync(SapQueryKinds.InvoiceLines, 1);

        var sql = db.SapDocumentQueries.Single(q => q.CompanyId == company.CompanyId && q.DocumentKind == SapQueryKinds.InvoiceLines).SqlText;
        // Giro por prioridad: mayorista interno, luego subgrupo del punto de venta, luego Notes; máx. 40 (GiroRecep).
        // U_GSP_GroupName no existe en OCRD de Depor (HANA: invalid column name).
        Assert.DoesNotContain("U_GSP_GroupName", sql);
        var nx = sql.IndexOf("\"U_U_NX_giro\"", StringComparison.Ordinal);
        var sub = sql.IndexOf("\"U_GSP_SubGroupName\"", StringComparison.Ordinal);
        var notes = sql.IndexOf("C0.\"Notes\"", StringComparison.Ordinal);
        Assert.True(nx > 0 && nx < sub && sub < notes, sql);
        Assert.Contains("40)", sql);
        // Modelo (código sin talla) para las guías agrupadas por modelo.
        Assert.Contains("I.\"U_GSP_REFERENCE\" AS \"model_code\"", sql);
        // Nombre corto del artículo (OITM.FrgnName) en vez de la descripción larga del SKU; sin él, la de la línea.
        Assert.Contains("COALESCE(NULLIF(TRIM(I.\"FrgnName\"), ''), L.\"Dscription\") AS \"item_name\"", sql);
        // Un solo nombre por modelo (el máximo de sus SKU), para no repetir el modelo con nombres distintos.
        Assert.Contains("MAX(", sql);
        Assert.Contains("AS \"model_name\"", sql);
    }

    [Fact]
    public async Task GetLines_MapeaContrato()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.InvoiceLines, LinesSql);
        hana.Results[LinesSql] = new IReadOnlyDictionary<string, object?>[] { LineRow(0, "A1", 2, 1.5m), LineRow(1, "B2", 3.5m, null) };

        var lines = await svc.GetLinesAsync(SapQueryKinds.InvoiceLines, 4567);

        Assert.Equal("Venta al por menor", lines.CustomerActivity);
        Assert.Equal("Talca", lines.DestinationCity);
        Assert.Equal(2, lines.Lines.Count);
        Assert.Equal(new SapDocumentLine(1, "B2", "Artículo B2", 3.5m, "UN", null), lines.Lines[1]);
        Assert.Equal(4567, (int)hana.LastParameters!.GetType().GetProperty("doc_entry")!.GetValue(hana.LastParameters)!);
    }

    [Fact]
    public async Task GetLines_ColumnasOpcionalesDeModelo()
    {
        var (svc, db, hana, company) = Build();
        await Configure(db, company.CompanyId, SapQueryKinds.InvoiceLines, LinesSql);
        var conModelo = LineRow(0, "761952C-805-7", 2, 1m);
        conModelo["model_code"] = "761952C-805";
        conModelo["model_name"] = "ZAPATILLA X";
        hana.Results[LinesSql] = new IReadOnlyDictionary<string, object?>[] { conModelo, LineRow(1, "B2", 1, null) };

        var lines = await svc.GetLinesAsync(SapQueryKinds.InvoiceLines, 1);

        Assert.Equal(("761952C-805", "ZAPATILLA X"), (lines.Lines[0].ModelCode, lines.Lines[0].ModelName));
        Assert.Equal(((string?)null, (string?)null), (lines.Lines[1].ModelCode, lines.Lines[1].ModelName));
    }

    [Fact]
    public async Task GetLines_SinConsulta_MensajeClaro()
    {
        var (svc, _, _, _) = Build(engine: "PostgreSQL");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GetLinesAsync(SapQueryKinds.InvoiceLines, 1));
        Assert.Contains("líneas de factura", ex.Message);
    }

    [Fact]
    public async Task ListQueries_Hana_SiembraTambienLineas()
    {
        var (svc, _, _, _) = Build();
        var list = await svc.ListQueriesAsync();
        Assert.Contains(list, q => q.DocumentKind == SapQueryKinds.InvoiceLines && q.SqlText.Contains(":doc_entry"));
    }
}
