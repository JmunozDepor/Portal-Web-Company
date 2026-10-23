using Modulo.Despachos.Data;
using Modulo.Despachos.Dominio.Tarifas;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Dominio.Tarifas;
using Modulo.Despachos.Tests.Fakes;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Entorno común de los tests de manifiesto: maestros sembrados, tarifa base y reloj fijo.</summary>
internal sealed class ManifestTestEnv
{
    public const string IssuerRut = "76123456-0";
    public static readonly DateOnly Fecha = new(2026, 11, 10);

    public required DespachosDbContext Db { get; init; }
    public required FakeCurrentCompanyAccessor Company { get; init; }
    public required FakeCurrentUserContext User { get; init; }
    public required MasterDataService Master { get; init; }
    public required TariffService Tariff { get; init; }
    public required FakeSapDocumentLookupService Sap { get; init; }
    public required FixedTimeProvider Clock { get; init; }
    public required ManifestCostService Cost { get; init; }
    public required ManifestService Svc { get; init; }
    public required FakeNotificationService Notifications { get; init; }
    public required MonthlyClosureService Closure { get; init; }
    public required FakeDteEmissionService Dte { get; init; }
    /// <summary>Guías Res. 154 (deshabilitadas por defecto: ver <see cref="EnableGuidesAsync"/>).</summary>
    public required TransportGuideService Guides { get; init; }
    /// <summary>Escritura del folio en la transferencia SAP (Fase B).</summary>
    public required FakeSapTransferWriter Writer { get; init; }
    /// <summary>Agrupaciones (agrupación de transferencias por sucursal).</summary>
    public required DispatchGroupService Groups { get; init; }

    public long CarrierId, VehicleId, TrailerId, DriverId, OriginId, ShiftId, SmallTypeId;
    public long RancaguaId, CuricoId;

    public delegate ManifestService ServiceFactory(
        DespachosDbContext db, ICurrentCompanyAccessor company, ICurrentUserContext user,
        ISapDocumentLookupService sap, IMasterDataService master, IManifestCostService cost, TimeProvider time,
        INotificationService notifications, IMonthlyClosureService closure);

    public static ManifestTestEnv Build(
        bool seedRates = true, DespachosDbContext? db = null, Guid? companyId = null, ServiceFactory? factory = null)
    {
        db ??= TestDb.Create();
        var company = new FakeCurrentCompanyAccessor();
        if (companyId != null) company.CompanyId = companyId.Value;
        var user = new FakeCurrentUserContext();
        var master = new MasterDataService(db, company, user);
        var tariff = new TariffService(db, company, user, TarifasTestData.Engine(),
            new IRateCalculator[]
            {
                new FixedPerTripCalculator(), new PerM3Calculator(), new PerKmCalculator(),
                new PerPackageCalculator(), new PerDocumentCalculator(), new NoChargeCalculator()
            },
            new ISurchargeCondition[]
            {
                new DistinctZonesOverCondition(), new DistinctCommunesOverCondition(), new DepartureTimeFromCondition(),
                new ShiftIsCondition(), new WeekdayInCondition(), new IsHolidayCondition(),
                new DocumentsOverCondition(), new PackagesOverCondition(), new VehicleTypeIsCondition(),
                new ManualQuantityCondition()
            });
        var sap = new FakeSapDocumentLookupService();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 10, 15, 0, 0, TimeSpan.Zero)); // 12:00 Chile
        var closure = new MonthlyClosureService(db, company, user, clock);
        var cost = new ManifestCostService(db, company, tariff, TarifasTestData.Engine(), clock, closure);
        var notifications = new FakeNotificationService();
        var dte = new FakeDteEmissionService { IssuerTaxId = IssuerRut };
        var writer = new FakeSapTransferWriter();
        var guides = new TransportGuideService(db, company, user, sap, dte, clock, notifications, writer: writer);
        var svc = factory != null
            ? factory(db, company, user, sap, master, cost, clock, notifications, closure)
            : new ManifestService(db, company, user, sap, master, cost, clock, notifications, closure, guides: guides);
        var env = new ManifestTestEnv
        {
            Db = db, Company = company, User = user, Master = master, Tariff = tariff, Sap = sap,
            Clock = clock, Cost = cost, Svc = svc, Notifications = notifications, Closure = closure, Dte = dte, Guides = guides, Writer = writer,
            Groups = new DispatchGroupService(db, company, user, sap, master, clock, notifications),
        };
        env.SeedAsync(seedRates).GetAwaiter().GetResult();
        return env;
    }

    private async Task SeedAsync(bool seedRates)
    {
        var communes = await Master.ListCommunesAsync();
        RancaguaId = communes.First(c => c.Name == "Rancagua").Id;
        CuricoId = communes.First(c => c.Name == "Curicó").Id;
        SmallTypeId = await Master.SaveVehicleTypeAsync(new VehicleType { Code = "S", Name = "Chico", CapacityM3 = 10 });
        CarrierId = await Master.SaveCarrierAsync(new Carrier { Code = "TSM", Name = "TSM", TaxId = "76543210-3" });
        VehicleId = await Master.SaveVehicleAsync(new Vehicle { Plate = "AB-1234", VehicleTypeId = SmallTypeId, CarrierId = CarrierId });
        TrailerId = await Master.SaveVehicleAsync(new Vehicle { Plate = "CD-5678", CarrierId = CarrierId, IsTrailer = true });
        DriverId = await Master.SaveDriverAsync(new Driver
        {
            TaxId = "12345678-5", FullName = "Juan Pérez", CarrierId = CarrierId,
            LicenseExpiresAt = new DateOnly(2027, 1, 1),
        });
        OriginId = await Master.SaveOriginAsync(new Origin { Code = "CD", Name = "CD Rancagua", Address = "Ruta 5 km 90", CommuneId = RancaguaId });
        ShiftId = await Master.SaveShiftAsync(new Shift { Code = "D", Name = "Diurno" });
        await Master.SaveIssuerTaxIdAsync(IssuerRut);
        if (seedRates)
        {
            await Tariff.SaveRateCardAsync(new RateCard { CommuneId = RancaguaId, Amount = 150000, ValidFrom = new DateOnly(2026, 1, 1) });
            await Tariff.SaveRateCardAsync(new RateCard { CommuneId = CuricoId, Amount = 220000, ValidFrom = new DateOnly(2026, 1, 1) });
        }
    }

    public ManifestHeaderInput Header(DateOnly? date = null, long? vehicleId = null, long? driverId = null,
        TimeOnly? planned = null, DateOnly? arrival = null) =>
        new(date ?? Fecha, ShiftId, OriginId, CarrierId, vehicleId ?? VehicleId, TrailerId, driverId ?? DriverId,
            arrival, null, planned, "OS-1", "obs", 1000);

    /// <summary>Activa "emitir guías al despachar" en la compañía del entorno.</summary>
    public async Task EnableGuidesAsync()
    {
        var s = Db.Settings.Single(x => x.CompanyId == Company.CompanyId);
        s.GuidesEnabled = true;
        await Db.SaveChangesAsync();
    }

    /// <summary>Líneas SAP de la factura <paramref name="folio"/> (DocEntry = folio + 1000, como <see cref="Doc"/>).</summary>
    public void AddLines(long folio, params (string Item, decimal Qty, decimal? Kg)[] items) =>
        Sap.Lines[(int)folio + 1000] = new SapDocumentLines("Venta al por menor", "Rancagua",
            items.Select((x, i) => new SapDocumentLine(i, x.Item, $"Artículo {x.Item}", x.Qty, "UN", x.Kg)).ToList());

    /// <summary>Borrador con fecha de llegada y facturas con líneas, listo para emitir guías.</summary>
    public async Task<Guid> GuideReadyDraftAsync(params long[] folios)
    {
        var id = await Svc.CreateDraftAsync(Header(arrival: Fecha));
        foreach (var f in folios)
        {
            if (!Sap.Docs.Any(d => d.Folio == f)) AddSap(f);
            if (!Sap.Lines.ContainsKey((int)f + 1000)) AddLines(f, ("ART-" + f, 2, 1.5m));
            var r = await Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, f));
            Assert.Equal("added", r.Outcome);
        }
        return id;
    }

    public SapDocumentInfo Doc(long folio, string kind = DocumentKinds.Invoice, int sii = 33, string? commune = "Rancagua",
        decimal units = 10, int packages = 4, bool canceled = false, DateOnly? date = null, decimal total = 119000) =>
        new(kind, sii, folio, (int)folio + 1000, (int)folio + 2000, date ?? new DateOnly(2026, 11, 9),
            "C001", "Cliente SA", "11111111-1", "Calle 1 #23", commune, "OC-77", 555, units, packages, total, canceled, null, null);

    public SapDocumentInfo AddSap(long folio, string kind = DocumentKinds.Invoice, int sii = 33, string? commune = "Rancagua",
        bool canceled = false, DateOnly? date = null, decimal total = 119000)
    {
        var d = Doc(folio, kind, sii, commune, canceled: canceled, date: date, total: total);
        Sap.Docs.Add(d);
        return d;
    }

    public static string Ted(string issuer, int td, long folio, decimal amount = 119000) =>
        $"\r\n <TED version=\"1.0\"><DD><RE>{issuer}</RE><TD>{td}</TD><F>{folio}</F><FE>2026-11-09</FE>" +
        $"<RR>11111111-1</RR><RSR>Cliente SA</RSR><MNT>{amount}</MNT></DD></TED>\r\n";

    public async Task<Guid> DraftWithDocsAsync(params long[] folios)
    {
        var id = await Svc.CreateDraftAsync(Header());
        foreach (var f in folios)
        {
            if (!Sap.Docs.Any(d => d.Folio == f)) AddSap(f);
            var r = await Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, f));
            Assert.Equal("added", r.Outcome);
        }
        return id;
    }
}
