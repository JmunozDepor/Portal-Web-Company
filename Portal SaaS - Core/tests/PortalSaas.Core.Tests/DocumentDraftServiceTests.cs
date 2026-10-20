using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Core.Documentos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class UsuarioBorrador : ICurrentUserContext
{
    public required Guid UserId { get; init; }
    public string Username { get; init; } = "usuario";
    public bool IsAdmin { get; init; }
    public required Guid OrganizationId { get; init; }

    public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
}

file sealed class CompaniaBorrador : ICurrentCompanyAccessor
{
    public Guid CompanyId { get; init; }
    public string Code => "C1";
    public string Database => "DB";
    public string ServiceLayerUrl => "https://sl";
    public string Country => "CL";
    public bool HasCompany => true;
}

internal sealed class CampanaFalsaBorradores : INotificationService
{
    public List<(Guid UserId, string Title)> Enviadas { get; } = [];

    public Task<Guid> EnqueueAsync(Guid userId, string title, string message, string severity = NotificationSeverity.Info, string? url = null,
        CancellationToken ct = default) => throw new NotSupportedException();

    public Task<Guid?> TryEnqueueAsync(Guid userId, string title, string message, string severity = NotificationSeverity.Info, string? url = null,
        CancellationToken ct = default)
    {
        Enviadas.Add((userId, title));
        return Task.FromResult<Guid?>(Guid.NewGuid());
    }

    public Task<IReadOnlyList<NotificationDto>> ListRecentAsync(Guid userId, int limit = 20, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task MarkAsReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
}

public class DocumentDraftServiceTests
{
    private static readonly Guid Org = Guid.NewGuid();

    private static async Task<(PortalSaasDbContext Db, Guid CompanyId)> CrearAsync()
    {
        var db = new PortalSaasDbContext(new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var company = new Company
        {
            OrganizationId = Org, Code = "C1", Name = "Compañía", DatabaseName = "DB", ServiceLayerUrl = "https://sl",
            IntegrationUsername = "u", IntegrationSecretKey = "k", Country = "CL",
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return (db, company.Id);
    }

    private static DocumentDraftService Servicio(PortalSaasDbContext db, Guid companyId, Guid userId, CampanaFalsaBorradores? campana = null, string nombre = "usuario") =>
        new(db, new UsuarioBorrador { UserId = userId, OrganizationId = Org, Username = nombre }, new CompaniaBorrador { CompanyId = companyId },
            campana ?? new CampanaFalsaBorradores());

    private static DocumentDraftSaveRequest Pedido(Guid? id = null, string motivo = "Esperando confirmación") =>
        new(id, DocumentDraftEngine.Inventory, "InventoryTransferRequest", motivo, "{}", "C100", "REF-1", new DateOnly(2026, 10, 20), 3, null);

    [Fact]
    public async Task Guardar_SinMotivo_Falla()
    {
        var (db, company) = await CrearAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Servicio(db, company, Guid.NewGuid()).SaveAsync(Pedido(motivo: "  ")));
    }

    [Fact]
    public async Task Guardar_GuardaCreadorYMotivo_YActualizarConservaElCreador()
    {
        var (db, company) = await CrearAsync();
        var creador = Guid.NewGuid();
        var id = await Servicio(db, company, creador, nombre: "ana").SaveAsync(Pedido());

        await Servicio(db, company, Guid.NewGuid(), nombre: "luis").SaveAsync(Pedido(id, "Falta stock en B01"));
        var draft = await Servicio(db, company, creador).GetAsync(id);

        Assert.NotNull(draft);
        Assert.Equal(creador, draft!.CreatedByUserId);
        Assert.Equal("ana", draft.CreatedByUsername);
        Assert.Equal("luis", draft.UpdatedByUsername);
        Assert.Equal("Falta stock en B01", draft.Reason);
        Assert.Equal(DocumentDraftStatus.Draft, draft.Status);
    }

    [Fact]
    public async Task Listar_PorDefectoSoloLosPropios_YConTodosVeLosDelResto()
    {
        var (db, company) = await CrearAsync();
        var yo = Guid.NewGuid();
        await Servicio(db, company, yo).SaveAsync(Pedido());
        await Servicio(db, company, Guid.NewGuid()).SaveAsync(Pedido());
        var servicio = Servicio(db, company, yo);

        var mios = await servicio.ListAsync(new DocumentDraftQuery(DocumentDraftEngine.Inventory, "InventoryTransferRequest"));
        var todos = await servicio.ListAsync(new DocumentDraftQuery(DocumentDraftEngine.Inventory, "InventoryTransferRequest", OnlyMine: false));

        Assert.Equal(1, mios.TotalRecords);
        Assert.Equal(2, todos.TotalRecords);
    }

    [Fact]
    public async Task Enviar_EsAtomico_NoSeTomaDosVeces_YEnviadoNoSeEdita()
    {
        var (db, company) = await CrearAsync();
        var servicio = Servicio(db, company, Guid.NewGuid());
        var id = await servicio.SaveAsync(Pedido());

        Assert.True(await servicio.TryBeginSendAsync(id));
        Assert.False(await servicio.TryBeginSendAsync(id));

        await servicio.MarkSentAsync(id, 555);
        var enviado = await servicio.GetAsync(id);

        Assert.Equal(DocumentDraftStatus.Sent, enviado!.Status);
        Assert.Equal(555, enviado.SapDocEntry);
        Assert.False(await servicio.TryBeginSendAsync(id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.SaveAsync(Pedido(id)));
    }

    [Fact]
    public async Task ErrorDeSap_VuelveAPendiente_YSePuedeReintentar()
    {
        var (db, company) = await CrearAsync();
        var servicio = Servicio(db, company, Guid.NewGuid());
        var id = await servicio.SaveAsync(Pedido());

        await servicio.TryBeginSendAsync(id);
        await servicio.MarkErrorAsync(id, "Sin stock en B01");

        var draft = await servicio.GetAsync(id);
        Assert.Equal(DocumentDraftStatus.Error, draft!.Status);
        Assert.Equal("Sin stock en B01", draft.ErrorMessage);
        Assert.True(await servicio.TryBeginSendAsync(id));
    }

    [Fact]
    public async Task EnviandoInterrumpido_MasDeDiezMinutos_SePuedeRetomar()
    {
        var (db, company) = await CrearAsync();
        var servicio = Servicio(db, company, Guid.NewGuid());
        var id = await servicio.SaveAsync(Pedido());
        await servicio.TryBeginSendAsync(id);

        var fila = await db.DocumentDrafts.FirstAsync(d => d.Id == id);
        fila.UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-11);
        await db.SaveChangesAsync();

        Assert.True(await servicio.TryBeginSendAsync(id));
    }

    [Fact]
    public async Task OtroUsuarioEnviaOEliminaMiBorrador_MeAvisaPorLaCampana()
    {
        var (db, company) = await CrearAsync();
        var creador = Guid.NewGuid();
        var campana = new CampanaFalsaBorradores();
        var primero = await Servicio(db, company, creador).SaveAsync(Pedido());
        var segundo = await Servicio(db, company, creador).SaveAsync(Pedido());
        var otro = Servicio(db, company, Guid.NewGuid(), campana, "luis");

        await otro.TryBeginSendAsync(primero);
        await otro.MarkSentAsync(primero, 10);
        await otro.DeleteAsync(segundo);

        Assert.Equal(2, campana.Enviadas.Count);
        Assert.All(campana.Enviadas, n => Assert.Equal(creador, n.UserId));
    }

    [Fact]
    public async Task ElCreadorEliminaSuBorrador_SinAviso()
    {
        var (db, company) = await CrearAsync();
        var yo = Guid.NewGuid();
        var campana = new CampanaFalsaBorradores();
        var servicio = Servicio(db, company, yo, campana);
        var id = await servicio.SaveAsync(Pedido());

        await servicio.DeleteAsync(id);

        Assert.Null(await servicio.GetAsync(id));
        Assert.Empty(campana.Enviadas);
    }

    [Fact]
    public async Task Limpieza_RespetaLaRetencionDeCadaCompania_YNuncaBorraUnoEnviando()
    {
        var (db, company) = await CrearAsync();
        var otraCompania = new Company
        {
            OrganizationId = Org, Code = "C2", Name = "Sin retención", DatabaseName = "DB2", ServiceLayerUrl = "https://sl",
            IntegrationUsername = "u", IntegrationSecretKey = "k", Country = "CL",
        };
        db.Companies.Add(otraCompania);
        db.CompanyDocumentSettings.Add(new CompanyDocumentSettings { CompanyId = company, DraftRetentionDays = 30 });
        var viejo = DateTimeOffset.UtcNow.AddDays(-40);
        DocumentDraft Fila(Guid companyId, string status, DateTimeOffset updated) => new()
        {
            CompanyId = companyId, Engine = "inventario", DocumentType = "X", Status = status, Reason = "r", PayloadJson = "{}",
            CreatedByUsername = "u", UpdatedByUsername = "u", UpdatedAt = updated,
        };
        db.DocumentDrafts.AddRange(
            Fila(company, DocumentDraftStatus.Draft, viejo),
            Fila(company, DocumentDraftStatus.Sent, viejo),
            Fila(company, DocumentDraftStatus.Sending, viejo),
            Fila(company, DocumentDraftStatus.Draft, DateTimeOffset.UtcNow),
            Fila(otraCompania.Id, DocumentDraftStatus.Draft, viejo));
        await db.SaveChangesAsync();

        var eliminados = await DocumentDraftCleanupWorker.CleanAsync(db, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(2, eliminados);
        Assert.Equal(3, await db.DocumentDrafts.CountAsync());
        Assert.True(await db.DocumentDrafts.AnyAsync(d => d.Status == DocumentDraftStatus.Sending));
        Assert.True(await db.DocumentDrafts.AnyAsync(d => d.CompanyId == otraCompania.Id));
    }
}
