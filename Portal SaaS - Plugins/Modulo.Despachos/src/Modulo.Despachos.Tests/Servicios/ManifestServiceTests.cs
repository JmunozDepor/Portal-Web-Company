using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Tests.Servicios;

public class ManifestServiceTests
{
    // ------------------------------------------------------------ Numeración

    [Fact]
    public async Task GetByNumber_EncuentraPorCorrelativo_YNullSiNoExiste()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var number = (await env.Svc.GetAsync(id))!.Number;
        Assert.Equal(id, (await env.Svc.GetByNumberAsync(number))!.Id);
        Assert.Null(await env.Svc.GetByNumberAsync(number + 99));
    }

    [Fact]
    public async Task CreateDraft_NumeraCorrelativoPorCompania()
    {
        var name = "despachos-" + Guid.NewGuid();
        var a = ManifestTestEnv.Build(db: TestDb.Create(name));
        var m1 = await a.Svc.GetAsync(await a.Svc.CreateDraftAsync(a.Header()));
        var m2 = await a.Svc.GetAsync(await a.Svc.CreateDraftAsync(a.Header()));
        Assert.Equal(1, m1!.Number);
        Assert.Equal(2, m2!.Number);
        Assert.Equal(ManifestStatuses.Draft, m1.Status);

        // Otra compañía en la misma base arranca en 1.
        var b = ManifestTestEnv.Build(db: TestDb.Create(name), companyId: Guid.NewGuid());
        var mb = await b.Svc.GetAsync(await b.Svc.CreateDraftAsync(b.Header()));
        Assert.Equal(1, mb!.Number);
    }

    private sealed class RacingManifestService : ManifestService
    {
        public int SaveCalls;
        public Func<Task>? BeforeFirstSave;

        public RacingManifestService(DespachosDbContext db, ICurrentCompanyAccessor c, ICurrentUserContext u,
            ISapDocumentLookupService s, IMasterDataService m, IManifestCostService cost, TimeProvider t,
            INotificationService n, IMonthlyClosureService closure)
            : base(db, c, u, s, m, cost, t, n, closure) { }

        protected override async Task SaveNewManifestAsync()
        {
            SaveCalls++;
            if (SaveCalls == 1 && BeforeFirstSave != null)
            {
                await BeforeFirstSave();
                // Simula la violación del índice único (company_id, number): InMemory no lo hace cumplir.
                throw new DbUpdateException("duplicate key (company_id, number)");
            }
            await base.SaveNewManifestAsync();
        }
    }

    [Fact]
    public async Task CreateDraft_ConflictoDeNumero_ReintentaYAsignaElSiguiente()
    {
        var name = "despachos-" + Guid.NewGuid();
        RacingManifestService? racing = null;
        var env = ManifestTestEnv.Build(db: TestDb.Create(name), factory: (db, c, u, s, m, cost, t, n, cl) =>
            racing = new RacingManifestService(db, c, u, s, m, cost, t, n, cl));
        await env.Svc.CreateDraftAsync(env.Header());
        await env.Svc.CreateDraftAsync(env.Header());
        racing!.SaveCalls = 0;

        // Entre la lectura del MAX y la escritura, otro usuario toma el número 3.
        racing.BeforeFirstSave = async () =>
        {
            await using var other = TestDb.Create(name);
            other.Manifests.Add(new Manifest
            {
                Id = Guid.NewGuid(), CompanyId = env.Company.CompanyId, Number = 3, Status = ManifestStatuses.Draft,
                DispatchDate = ManifestTestEnv.Fecha, CostStatus = "ok",
            });
            await other.SaveChangesAsync();
        };

        var id = await env.Svc.CreateDraftAsync(env.Header());
        var m = await env.Svc.GetAsync(id);
        Assert.Equal(4, m!.Number);
        Assert.Equal(2, racing.SaveCalls);
        Assert.Equal(4, await env.Db.Manifests.CountAsync(x => x.CompanyId == env.Company.CompanyId));
    }

    private sealed class AlwaysConflictService : ManifestService
    {
        public int SaveCalls;
        public AlwaysConflictService(DespachosDbContext db, ICurrentCompanyAccessor c, ICurrentUserContext u,
            ISapDocumentLookupService s, IMasterDataService m, IManifestCostService cost, TimeProvider t,
            INotificationService n, IMonthlyClosureService closure)
            : base(db, c, u, s, m, cost, t, n, closure) { }
        protected override Task SaveNewManifestAsync()
        {
            SaveCalls++;
            throw new DbUpdateException("duplicate key");
        }
    }

    [Fact]
    public async Task CreateDraft_ConflictoPersistente_FallaTrasTresIntentos()
    {
        var env = ManifestTestEnv.Build(factory: (db, c, u, s, m, cost, t, n, cl) => new AlwaysConflictService(db, c, u, s, m, cost, t, n, cl));
        await Assert.ThrowsAsync<DbUpdateException>(() => env.Svc.CreateDraftAsync(env.Header()));
        Assert.Equal(3, ((AlwaysConflictService)env.Svc).SaveCalls);
    }

    [Fact]
    public async Task CreateDraft_ValidaMaestros()
    {
        var env = ManifestTestEnv.Build();
        var bad = env.Header() with { VehicleId = 9999 };
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.CreateDraftAsync(bad));
        Assert.Empty(env.Db.Manifests);
    }

    // ------------------------------------------------------------ Agregar documentos

    [Fact]
    public async Task AddDocument_PorTed_ResuelveTipoYFolio()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(154321);
        env.Sap.Docs.Add(env.Doc(154321, DocumentKinds.TransferGuide, 52)); // mismo folio en otro tipo
        var id = await env.Svc.CreateDraftAsync(env.Header());

        var r = await env.Svc.AddDocumentAsync(id, new DocumentCapture(
            ManifestTestEnv.Ted("76123456-0", 33, 154321), null, null));

        Assert.Equal("added", r.Outcome);
        Assert.Contains((DocumentKinds.Invoice, 154321L, (int?)33), env.Sap.Calls);
        var m = await env.Svc.GetAsync(id);
        var d = Assert.Single(m!.Documents);
        Assert.Equal(CaptureMethods.Pdf417, d.CaptureMethod);
        Assert.Equal(DocumentKinds.Invoice, d.DocumentKind);
        Assert.Equal(33, d.SiiDocumentType);
        Assert.Equal("Cliente SA", d.CustomerName);
        Assert.Equal("11111111-1", d.CustomerTaxId);
        Assert.Equal("Calle 1 #23", d.DestinationAddress);
        Assert.Equal(env.RancaguaId, d.DestinationCommuneId);
        Assert.Equal(10m, d.Units);
        Assert.Equal(4, d.Packages);
        Assert.Equal("OC-77", d.CustomerPo);
        Assert.Equal(555, d.SapBaseOrderEntry);
        Assert.Equal(DeliveryStatuses.Pending, d.DeliveryStatus);
        Assert.Contains(m.Events, e => e.EventType == ManifestEventTypes.DocumentAdded && e.ManifestDocumentId == d.Id);
    }

    [Fact]
    public async Task AddDocument_PorTed_ConRuidoDelEscaner_Funciona()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(777);
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var r = await env.Svc.AddDocumentAsync(id, new DocumentCapture("  basura " + ManifestTestEnv.Ted(ManifestTestEnv.IssuerRut, 33, 777) + " \r\n", null, null));
        Assert.Equal("added", r.Outcome);
    }

    [Fact]
    public async Task AddDocument_TedBasura_InvalidScan()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var r = await env.Svc.AddDocumentAsync(id, new DocumentCapture("hola no soy un TED", null, null));
        Assert.Equal("invalid_scan", r.Outcome);
        Assert.Empty((await env.Svc.GetAsync(id))!.Documents);
    }

    [Fact]
    public async Task AddDocument_TedDeOtroEmisor_OtherIssuer()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(154321);
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var r = await env.Svc.AddDocumentAsync(id, new DocumentCapture(ManifestTestEnv.Ted("99999999-9", 33, 154321), null, null));
        Assert.Equal("other_issuer", r.Outcome);
        Assert.Empty((await env.Svc.GetAsync(id))!.Documents);
        Assert.Empty(env.Sap.Calls);
    }

    [Fact]
    public async Task AddDocument_TedSinRutEmisorConfigurado_Lanza_PeroManualFunciona()
    {
        var env = ManifestTestEnv.Build();
        await env.Master.SaveIssuerTaxIdAsync(null);
        env.AddSap(5);
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.AddDocumentAsync(id, new DocumentCapture(ManifestTestEnv.Ted(ManifestTestEnv.IssuerRut, 33, 5), null, null)));
        Assert.Contains("RUT emisor", ex.Message);

        var r = await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 5));
        Assert.Equal("added", r.Outcome);
        Assert.Equal(CaptureMethods.Manual, (await env.Svc.GetAsync(id))!.Documents.Single().CaptureMethod);
    }

    [Fact]
    public async Task AddDocument_TedMontoDistintoDeSap_Warning()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(9, total: 119000);
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var r = await env.Svc.AddDocumentAsync(id, new DocumentCapture(ManifestTestEnv.Ted(ManifestTestEnv.IssuerRut, 33, 9, 5000), null, null));
        Assert.Equal("added", r.Outcome);
        Assert.Contains(r.Warnings, w => w.Contains("monto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddDocument_Repetido_DuplicateInThis_NoError()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(100);
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var cap = new DocumentCapture(null, DocumentKinds.Invoice, 100);
        Assert.Equal("added", (await env.Svc.AddDocumentAsync(id, cap)).Outcome);
        var again = await env.Svc.AddDocumentAsync(id, cap);
        Assert.Equal("duplicate_in_this", again.Outcome);
        Assert.Single((await env.Svc.GetAsync(id))!.Documents);
    }

    [Fact]
    public async Task AddDocument_EnOtroManifiestoActivo_InOtherManifest()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(100);
        var a = await env.Svc.CreateDraftAsync(env.Header());
        var b = await env.Svc.CreateDraftAsync(env.Header());
        var cap = new DocumentCapture(null, DocumentKinds.Invoice, 100);
        await env.Svc.AddDocumentAsync(a, cap);

        var r = await env.Svc.AddDocumentAsync(b, cap);
        Assert.Equal("in_other_manifest", r.Outcome);
        Assert.Equal(1, r.OtherManifestNumber);
        Assert.Empty((await env.Svc.GetAsync(b))!.Documents);
    }

    [Fact]
    public async Task AddDocument_OtroAnuladoODocumentoNoEntregado_Added()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(100);
        env.AddSap(200);
        var a = await env.Svc.CreateDraftAsync(env.Header());
        var b = await env.Svc.CreateDraftAsync(env.Header());
        await env.Svc.AddDocumentAsync(a, new DocumentCapture(null, DocumentKinds.Invoice, 100));
        await env.Svc.AddDocumentAsync(a, new DocumentCapture(null, DocumentKinds.Invoice, 200));

        // 100: el manifiesto A queda anulado; 200: el documento allí quedó no entregado.
        (await env.Db.Manifests.FirstAsync(m => m.Id == a)).Status = ManifestStatuses.Annulled;
        await env.Db.SaveChangesAsync();
        Assert.Equal("added", (await env.Svc.AddDocumentAsync(b, new DocumentCapture(null, DocumentKinds.Invoice, 100))).Outcome);

        (await env.Db.Manifests.FirstAsync(m => m.Id == a)).Status = ManifestStatuses.Dispatched;
        (await env.Db.ManifestDocuments.FirstAsync(d => d.ManifestId == a && d.Folio == 200)).DeliveryStatus = DeliveryStatuses.NotDelivered;
        await env.Db.SaveChangesAsync();
        Assert.Equal("added", (await env.Svc.AddDocumentAsync(b, new DocumentCapture(null, DocumentKinds.Invoice, 200))).Outcome);
    }

    [Fact]
    public async Task AddDocument_ComunaNoResuelta_AgregaConWarning()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(100, commune: "Villa Inventada");
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var r = await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 100));
        Assert.Equal("added", r.Outcome);
        Assert.Contains(r.Warnings, w => w.Contains("comuna", StringComparison.OrdinalIgnoreCase));
        var d = (await env.Svc.GetAsync(id))!.Documents.Single();
        Assert.Null(d.DestinationCommuneId);
        Assert.Equal("Villa Inventada", d.DestinationCommuneName);
    }

    [Fact]
    public async Task AddDocument_FechaPosteriorADespacho_Warning()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(100, date: new DateOnly(2026, 11, 12));
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var r = await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 100));
        Assert.Equal("added", r.Outcome);
        Assert.Contains(r.Warnings, w => w.Contains("posterior", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddDocument_AnuladoEnSap_Canceled_YInexistente_NotFound()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(100, canceled: true);
        var id = await env.Svc.CreateDraftAsync(env.Header());
        Assert.Equal("canceled", (await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 100))).Outcome);
        Assert.Equal("not_found", (await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 101))).Outcome);
    }

    [Fact]
    public async Task AddDocument_FolioEnDosTipos_Ambiguous()
    {
        var env = ManifestTestEnv.Build();
        env.Sap.Docs.Add(env.Doc(1500, DocumentKinds.Invoice, 33));
        env.Sap.Docs.Add(env.Doc(1500, DocumentKinds.Invoice, 34));
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var r = await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 1500));
        Assert.Equal("ambiguous", r.Outcome);
        Assert.Contains(r.Warnings, w => w.Contains("indique el tipo"));
        Assert.Empty((await env.Svc.GetAsync(id))!.Documents);
    }

    [Fact]
    public async Task AddDocument_ManifiestoNoBorrador_Lanza()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.DraftWithDocsAsync(100);
        Assert.True((await env.Svc.DispatchAsync(id)).Success);
        env.AddSap(101);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 101)));
    }

    [Fact]
    public async Task RemoveDocument_QuitaYRegistraEvento_SoloEnBorrador()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.DraftWithDocsAsync(100, 101);
        var m = await env.Svc.GetAsync(id);
        var doc = m!.Documents.First(d => d.Folio == 100);
        await env.Svc.RemoveDocumentAsync(id, doc.Id);

        m = await env.Svc.GetAsync(id);
        Assert.Single(m!.Documents);
        Assert.Contains(m.Events, e => e.EventType == ManifestEventTypes.DocumentRemoved && e.ManifestDocumentId == doc.Id);

        Assert.True((await env.Svc.DispatchAsync(id)).Success);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.RemoveDocumentAsync(id, m.Documents.Single().Id));
    }

    // ------------------------------------------------------------ Despacho

    [Fact]
    public async Task Dispatch_ValidaChoferTransportistaYDocumentos()
    {
        var env = ManifestTestEnv.Build();

        // Sin documentos.
        var empty = await env.Svc.CreateDraftAsync(env.Header());
        var r0 = await env.Svc.DispatchAsync(empty);
        Assert.False(r0.Success);
        Assert.Contains(r0.Errors, e => e.Contains("al menos un documento"));

        // Chofer con RUT inválido (insertado directo: el maestro no lo permitiría).
        var badDriver = new Driver { CompanyId = env.Company.CompanyId, TaxId = "11111111-2", FullName = "Malo", CarrierId = env.CarrierId, IsActive = true, CreatedAt = DateTime.UtcNow };
        env.Db.Drivers.Add(badDriver);
        await env.Db.SaveChangesAsync();
        env.AddSap(1);
        var id = await env.Svc.CreateDraftAsync(env.Header(driverId: badDriver.Id));
        await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 1));
        var r1 = await env.Svc.DispatchAsync(id);
        Assert.False(r1.Success);
        Assert.Contains(r1.Errors, e => e.Contains("RUT", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ManifestStatuses.Draft, (await env.Svc.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Dispatch_ChoferDeOtroTransportista_YLicenciaVencida_SonWarnings()
    {
        var env = ManifestTestEnv.Build();
        var otro = await env.Master.SaveCarrierAsync(new Carrier { Code = "MTO", Name = "MTO", TaxId = "76543210-3" });
        var driver = await env.Master.SaveDriverAsync(new Driver
        {
            TaxId = "7654321-6", FullName = "Pedro", CarrierId = otro, LicenseExpiresAt = new DateOnly(2026, 11, 1),
        });
        env.AddSap(1);
        var id = await env.Svc.CreateDraftAsync(env.Header(driverId: driver));
        await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 1));
        var r = await env.Svc.DispatchAsync(id);
        Assert.True(r.Success);
        Assert.Contains(r.Warnings, w => w.Contains("transportista", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(r.Warnings, w => w.Contains("licencia", StringComparison.OrdinalIgnoreCase));
    }

    private static Task AddLateSurchargeAsync(ManifestTestEnv env) =>
        env.Tariff.SaveSurchargeAsync(new RateSurcharge
        {
            Name = "Salida tardía", ConditionType = "departure_time_from", ConditionParametersJson = "{\"time\":\"17:00\"}",
            EffectType = "percent_of_base", Value = 20, ValidFrom = new DateOnly(2026, 1, 1),
        });

    [Fact]
    public async Task Dispatch_GuardaUtc_YRecargoUsaHoraChile()
    {
        var env = ManifestTestEnv.Build();
        await AddLateSurchargeAsync(env);
        env.Clock.Now = new DateTimeOffset(2026, 11, 10, 20, 42, 0, TimeSpan.Zero); // 17:42 en Chile (UTC-3)
        var id = await env.DraftWithDocsAsync(100);

        var r = await env.Svc.DispatchAsync(id);
        Assert.True(r.Success);

        var m = (await env.Svc.GetAsync(id))!;
        Assert.Equal(DateTimeKind.Utc, m.DepartedAt!.Value.Kind);
        Assert.Equal(new DateTime(2026, 11, 10, 20, 42, 0, DateTimeKind.Utc), m.DepartedAt);
        Assert.Contains(m.CostLines, l => l.Kind == "surcharge");
        Assert.Equal(180000m, m.CostCalculated);
        Assert.Equal(180000m, m.CostFinal);
        Assert.Equal("ok", m.CostStatus);
    }

    [Fact]
    public async Task Dispatch_AntesDeLas17Chile_NoAplicaRecargo_AunqueUtcSeaMayorA17()
    {
        var env = ManifestTestEnv.Build();
        await AddLateSurchargeAsync(env);
        env.Clock.Now = new DateTimeOffset(2026, 11, 10, 18, 0, 0, TimeSpan.Zero); // 15:00 Chile
        var id = await env.DraftWithDocsAsync(100);
        Assert.True((await env.Svc.DispatchAsync(id)).Success);
        var m = (await env.Svc.GetAsync(id))!;
        Assert.DoesNotContain(m.CostLines, l => l.Kind == "surcharge");
        Assert.Equal(150000m, m.CostCalculated);
    }

    [Fact]
    public async Task Dispatch_TomaSnapshotRes154()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.DraftWithDocsAsync(100);
        Assert.True((await env.Svc.DispatchAsync(id)).Success);

        var m = (await env.Svc.GetAsync(id))!;
        Assert.Equal("76543210-3", m.CarrierTaxId);
        Assert.Equal("12345678-5", m.DriverTaxId);
        Assert.Equal("Juan Pérez", m.DriverName);
        Assert.Equal("AB1234", m.Plate);
        Assert.Equal("CD5678", m.TrailerPlate);
        Assert.Equal("Ruta 5 km 90", m.OriginAddress);
        Assert.Equal("Rancagua", m.OriginCommune);

        // Cambiar el maestro después no altera el manifiesto.
        var drv = (await env.Master.ListDriversAsync()).Single();
        drv.FullName = "Otro Nombre";
        await env.Master.SaveDriverAsync(drv);
        var veh = (await env.Master.ListVehiclesAsync()).First(v => v.Id == env.VehicleId);
        veh.Plate = "ZZ-9999";
        await env.Master.SaveVehicleAsync(veh);
        m = (await env.Svc.GetAsync(id))!;
        Assert.Equal("Juan Pérez", m.DriverName);
        Assert.Equal("AB1234", m.Plate);
    }

    [Fact]
    public async Task Dispatch_DocumentosPasanADispatched_YEventos()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.DraftWithDocsAsync(100, 101);
        var r = await env.Svc.DispatchAsync(id);
        Assert.True(r.Success);

        var m = (await env.Svc.GetAsync(id))!;
        Assert.Equal(ManifestStatuses.Dispatched, m.Status);
        Assert.All(m.Documents, d => Assert.Equal(DeliveryStatuses.Dispatched, d.DeliveryStatus));
        var ev = m.Events.Single(e => e.EventType == ManifestEventTypes.Dispatched && e.ManifestDocumentId == null);
        Assert.Equal(ManifestStatuses.Draft, ev.FromStatus);
        Assert.Equal(ManifestStatuses.Dispatched, ev.ToStatus);
        Assert.Equal(2, m.Events.Count(e => e.EventType == ManifestEventTypes.Dispatched && e.ManifestDocumentId != null));
        Assert.Equal(2, m.TotalDocuments);
        Assert.Equal(20m, m.TotalUnits);
        Assert.Equal(8, m.TotalPackages);
    }

    [Fact]
    public async Task Dispatch_BloqueaSiDocumentoQuedoEnOtroManifiestoActivo()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(100);
        var a = await env.Svc.CreateDraftAsync(env.Header());
        var b = await env.Svc.CreateDraftAsync(env.Header());
        await env.Svc.AddDocumentAsync(a, new DocumentCapture(null, DocumentKinds.Invoice, 100));
        // Carrera: el chequeo al agregar de B no vio A (otro usuario, mismo instante); se inserta la fila directo.
        var src = (await env.Svc.GetAsync(a))!.Documents.Single();
        env.Db.ManifestDocuments.Add(new ManifestDocument
        {
            Id = Guid.NewGuid(), CompanyId = src.CompanyId, ManifestId = b, DocumentKind = src.DocumentKind,
            SiiDocumentType = src.SiiDocumentType, Folio = src.Folio, SapDocEntry = src.SapDocEntry,
            DeliveryStatus = DeliveryStatuses.Pending, CaptureMethod = CaptureMethods.Manual,
            CustomerCode = "C", CustomerName = "X",
        });
        await env.Db.SaveChangesAsync();

        Assert.True((await env.Svc.DispatchAsync(a)).Success);
        var r = await env.Svc.DispatchAsync(b);
        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Contains("100") && e.Contains("N° 1"));
        Assert.Equal(ManifestStatuses.Draft, (await env.Svc.GetAsync(b))!.Status);
    }

    [Fact]
    public async Task Dispatch_SinTarifa_PermiteConCostStatusNoRate()
    {
        var env = ManifestTestEnv.Build(seedRates: false);
        var id = await env.DraftWithDocsAsync(100);
        var r = await env.Svc.DispatchAsync(id);
        Assert.True(r.Success);
        Assert.NotEmpty(r.Warnings);
        var m = (await env.Svc.GetAsync(id))!;
        Assert.Equal("no_rate", m.CostStatus);
        Assert.Null(m.CostCalculated);
        Assert.Equal(ManifestStatuses.Dispatched, m.Status);
    }

    [Fact]
    public async Task Dispatch_NoBorrador_Lanza()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.DraftWithDocsAsync(100);
        await env.Svc.DispatchAsync(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Svc.DispatchAsync(id));
    }

    // ------------------------------------------------------------ Cambio de vehículo / chofer

    [Fact]
    public async Task ChangeVehicle_PostDespacho_RenuevaSnapshotRecalculaYRegistraEvento()
    {
        var env = ManifestTestEnv.Build(seedRates: false);
        var big = await env.Master.SaveVehicleTypeAsync(new VehicleType { Code = "B", Name = "Grande", CapacityM3 = 30 });
        await env.Tariff.SaveRateCardAsync(new RateCard { CommuneId = env.RancaguaId, VehicleTypeId = env.SmallTypeId, Amount = 100000, ValidFrom = new DateOnly(2026, 1, 1) });
        await env.Tariff.SaveRateCardAsync(new RateCard { CommuneId = env.RancaguaId, VehicleTypeId = big, Amount = 150000, ValidFrom = new DateOnly(2026, 1, 1) });
        var newVeh = await env.Master.SaveVehicleAsync(new Vehicle { Plate = "XY-0001", VehicleTypeId = big, CarrierId = env.CarrierId });
        var newDrv = await env.Master.SaveDriverAsync(new Driver { TaxId = "7654321-6", FullName = "Pedro Soto", CarrierId = env.CarrierId });
        var id = await env.DraftWithDocsAsync(100);
        await env.Svc.DispatchAsync(id);
        Assert.Equal(100000m, (await env.Svc.GetAsync(id))!.CostCalculated);

        await env.Svc.ChangeVehicleOrDriverAsync(id, newVeh, null, newDrv, "Falla mecánica");

        var m = (await env.Svc.GetAsync(id))!;
        Assert.Equal("XY0001", m.Plate);
        Assert.Null(m.TrailerPlate);
        Assert.Equal("Pedro Soto", m.DriverName);
        Assert.Equal("7654321-6", m.DriverTaxId);
        Assert.Equal(150000m, m.CostCalculated);
        var ev = m.Events.Single(e => e.EventType == ManifestEventTypes.VehicleChanged);
        Assert.Contains("Falla mecánica", ev.PayloadJson);
    }

    [Fact]
    public async Task ChangeVehicle_DeOtroTransportista_MantieneTransportistaYAdvierte()
    {
        var env = ManifestTestEnv.Build(seedRates: false);
        var otro = await env.Master.SaveCarrierAsync(new Carrier { Code = "MTO", Name = "MTO", TaxId = "76543210-3" });
        await env.Tariff.SaveRateCardAsync(new RateCard { CommuneId = env.RancaguaId, CarrierId = env.CarrierId, Amount = 100000, ValidFrom = new DateOnly(2026, 1, 1) });
        await env.Tariff.SaveRateCardAsync(new RateCard { CommuneId = env.RancaguaId, CarrierId = otro, Amount = 999000, ValidFrom = new DateOnly(2026, 1, 1) });
        var veh = await env.Master.SaveVehicleAsync(new Vehicle { Plate = "OT-0001", CarrierId = otro });
        var id = await env.DraftWithDocsAsync(100);
        await env.Svc.DispatchAsync(id);

        var warnings = await env.Svc.ChangeVehicleOrDriverAsync(id, veh, null, env.DriverId, "Reemplazo");

        var m = (await env.Svc.GetAsync(id))!;
        Assert.Equal(env.CarrierId, m.CarrierId);
        Assert.Equal("OT0001", m.Plate);
        Assert.Equal(100000m, m.CostCalculated); // tarifa del transportista del manifiesto
        Assert.Contains(warnings, w => w.Contains("OT0001") && w.Contains("MTO") && w.Contains("distinto del transportista del manifiesto"));
    }

    [Fact]
    public async Task Dispatch_VehiculoDeOtroTransportista_Warning()
    {
        var env = ManifestTestEnv.Build();
        var otro = await env.Master.SaveCarrierAsync(new Carrier { Code = "MTO", Name = "MTO", TaxId = "76543210-3" });
        var veh = await env.Master.SaveVehicleAsync(new Vehicle { Plate = "OT-0001", CarrierId = otro });
        env.AddSap(1);
        var id = await env.Svc.CreateDraftAsync(env.Header(vehicleId: veh));
        await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 1));
        var r = await env.Svc.DispatchAsync(id);
        Assert.True(r.Success);
        Assert.Contains(r.Warnings, w => w.Contains("El vehículo OT0001 pertenece a MTO, distinto del transportista del manifiesto."));
        // El chofer sí es del transportista del vehículo? No: es de TSM -> también advierte del chofer.
        Assert.Contains(r.Warnings, w => w.Contains("no pertenece al transportista del vehículo"));
        Assert.Equal(env.CarrierId, (await env.Svc.GetAsync(id))!.CarrierId);
    }

    [Fact]
    public async Task Dispatch_ChoferDistintoAlTransportistaDelVehiculo_Warning_YSinWarningSiCoincideConElVehiculo()
    {
        var env = ManifestTestEnv.Build();
        var otro = await env.Master.SaveCarrierAsync(new Carrier { Code = "MTO", Name = "MTO", TaxId = "76543210-3" });
        var veh = await env.Master.SaveVehicleAsync(new Vehicle { Plate = "OT-0001", CarrierId = otro });
        var drvOtro = await env.Master.SaveDriverAsync(new Driver { TaxId = "7654321-6", FullName = "Pedro", CarrierId = otro });
        env.AddSap(1);
        env.AddSap(2);

        // Chofer de TSM con vehículo de TSM pero chofer de otro -> advierte el chofer.
        var a = await env.Svc.CreateDraftAsync(env.Header(driverId: drvOtro));
        await env.Svc.AddDocumentAsync(a, new DocumentCapture(null, DocumentKinds.Invoice, 1));
        var ra = await env.Svc.DispatchAsync(a);
        Assert.Contains(ra.Warnings, w => w.Contains("El chofer Pedro no pertenece al transportista del vehículo."));

        // Vehículo y chofer de MTO con manifiesto de TSM: solo advierte el vehículo.
        var b = await env.Svc.CreateDraftAsync(env.Header(vehicleId: veh, driverId: drvOtro));
        await env.Svc.AddDocumentAsync(b, new DocumentCapture(null, DocumentKinds.Invoice, 2));
        var rb = await env.Svc.DispatchAsync(b);
        Assert.Contains(rb.Warnings, w => w.Contains("El vehículo"));
        Assert.DoesNotContain(rb.Warnings, w => w.Contains("El chofer"));
    }

    [Fact]
    public async Task ChangeVehicle_SinMotivoOEnBorrador_Lanza()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.DraftWithDocsAsync(100);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.ChangeVehicleOrDriverAsync(id, env.VehicleId, null, env.DriverId, "x")); // borrador
        await env.Svc.DispatchAsync(id);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.ChangeVehicleOrDriverAsync(id, env.VehicleId, null, env.DriverId, "  ")); // sin motivo
    }

    // ------------------------------------------------------------ Borrado de maestros usados

    [Fact]
    public async Task DeleteVehicle_UsadoEnManifiesto_Bloqueado()
    {
        var env = ManifestTestEnv.Build();
        await env.Svc.CreateDraftAsync(env.Header());
        var r = await env.Master.DeleteVehicleAsync(env.VehicleId);
        Assert.False(r.Deleted);
        Assert.Contains("desactív", r.BlockedReason);
        var rt = await env.Master.DeleteVehicleAsync(env.TrailerId); // el carro también cuenta
        Assert.False(rt.Deleted);
    }

    [Fact]
    public async Task DeleteDriverYCarrier_UsadosEnManifiesto_Bloqueados()
    {
        var env = ManifestTestEnv.Build();
        await env.Svc.CreateDraftAsync(env.Header());
        var d = await env.Master.DeleteDriverAsync(env.DriverId);
        Assert.False(d.Deleted);
        Assert.Contains("desactív", d.BlockedReason);

        // Un transportista sin vehículos ni choferes, solo con manifiestos, también queda bloqueado.
        var solo = await env.Master.SaveCarrierAsync(new Carrier { Code = "SOLO", Name = "Solo", TaxId = "76543210-3" });
        var h = env.Header() with { CarrierId = solo };
        await env.Svc.CreateDraftAsync(h);
        var c = await env.Master.DeleteCarrierAsync(solo);
        Assert.False(c.Deleted);
        Assert.Contains("manifiestos", c.BlockedReason);
    }

    [Fact]
    public async Task DeleteVehicle_SinManifiestos_SePuedeEliminar()
    {
        var env = ManifestTestEnv.Build();
        var extra = await env.Master.SaveVehicleAsync(new Vehicle { Plate = "QQ-1111", CarrierId = env.CarrierId });
        Assert.True((await env.Master.DeleteVehicleAsync(extra)).Deleted);
    }

    // ------------------------------------------------------------ Listado

    [Fact]
    public async Task List_FiltraPorEstadoYFolio()
    {
        var env = ManifestTestEnv.Build();
        var a = await env.DraftWithDocsAsync(1234);
        await env.DraftWithDocsAsync(999);
        await env.Svc.DispatchAsync(a);

        Assert.Equal(2, (await env.Svc.ListAsync(new ManifestFilter())).Count);
        Assert.Single(await env.Svc.ListAsync(new ManifestFilter(Status: ManifestStatuses.Dispatched)));
        var byFolio = await env.Svc.ListAsync(new ManifestFilter(FolioContains: "234"));
        Assert.Equal(a, Assert.Single(byFolio).Id);
        Assert.Empty(await env.Svc.ListAsync(new ManifestFilter(From: new DateOnly(2027, 1, 1))));
    }
}
