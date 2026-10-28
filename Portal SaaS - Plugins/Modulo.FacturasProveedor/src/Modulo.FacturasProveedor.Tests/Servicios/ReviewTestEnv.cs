using Modulo.FacturasProveedor.Data;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Servicios;
using Modulo.FacturasProveedor.Tests.Dominio.Reglas;
using Modulo.FacturasProveedor.Tests.Fakes;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.FacturasProveedor.Tests.Servicios;

/// <summary>Arma el orquestador real con fakes de plataforma y SAP. Reloj: 27-10-2026 12:00 de Chile.</summary>
internal sealed class ReviewTestEnv
{
    public FacturasProveedorDbContext Db { get; } = TestDb.Create();
    public FakeDteInboxService Inbox { get; } = new();
    public FakeSapLookupService Sap { get; } = new FakeSapLookupService().AddSupplier().AddSupplier("77555010-K", "P77555010", "Inmobiliaria Los Robles SpA");
    public FakeNotificationService Notifications { get; } = new();
    public FakeCurrentCompanyAccessor Company { get; } = new();
    public FakeCurrentUserContext User { get; } = new();
    public FixedTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero));
    public InvoiceRuleEngine Engine { get; } = new(RuleTestData.Catalog());
    public ModuleSettingsService Settings { get; }
    public SupplierProfileService Profiles { get; }
    public ApproverGroupService Groups { get; }
    public InvoiceReviewService Reviews { get; }
    public Guid FinanceUser { get; } = Guid.NewGuid();

    public ReviewTestEnv()
    {
        Settings = new ModuleSettingsService(Db, Engine, Company, User, Clock);
        Profiles = new SupplierProfileService(Db, Sap, Engine, Company, User, Clock);
        Groups = new ApproverGroupService(Db, Company, User, Clock);
        Reviews = new InvoiceReviewService(Db, Inbox, Engine, Settings, Sap, Profiles, Groups, Notifications, Company, Clock);
    }

    /// <summary>Grupo de Finanzas con FinanceUser y "procesar desde" = 20-10-2026, en el modo pedido.</summary>
    public async Task<ReviewTestEnv> StartAsync(string mode = ModuleModes.Simulation, DateOnly? processFrom = null)
    {
        await Groups.SaveAsync(new ApproverGroupInput(null, "Finanzas", true, true, new[] { FinanceUser }), CancellationToken.None);
        await Settings.SaveGeneralAsync(new GeneralSettingsInput { ProcessFromDate = processFrom ?? new DateOnly(2026, 10, 20), Mode = mode },
            CancellationToken.None);
        return this;
    }

    public static DateTimeOffset ReceivedAt(int day, int hourUtc = 12) => new(2026, 10, day, hourUtc, 0, 0, TimeSpan.Zero);

    public DteReceivedDto Invoice(long folio = 18452, long total = 2_200_000, int day = 24, int hourUtc = 12, int[]? purchaseOrders = null,
        string taxId = "76412553-1", string responseStatus = DteResponseStatuses.None, string? responseAction = null, int dteType = 33,
        string? notRespondableReason = null) =>
        Inbox.Add(taxId, dteType, folio, total, ReceivedAt(day, hourUtc), purchaseOrders: purchaseOrders ?? new[] { 4512 },
            responseStatus: responseStatus, responseAction: responseAction, notRespondableReason: notRespondableReason);

    public Task<ReviewCycleResult> EvaluateAsync() => Reviews.EvaluatePendingAsync(CancellationToken.None);
}
