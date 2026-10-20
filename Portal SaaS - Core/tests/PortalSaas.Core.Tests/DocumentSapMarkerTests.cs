using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Documentos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class MarcadorFalso : IDocumentSapMarkerService
{
    public Dictionary<string, int> EnSap { get; } = [];
    public bool SapCaido { get; init; }

    public Task<DocumentSapMarker> GetMarkerAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(new DocumentSapMarker(null, key, DocumentSapMarkerService.CommentsTagFor(key)));

    public Task<int?> FindDocEntryAsync(string engine, string documentType, string key, CancellationToken ct = default) =>
        SapCaido ? throw new InvalidOperationException("SAP no responde") : Task.FromResult<int?>(EnSap.TryGetValue(key, out var e) ? e : null);
}

public class DocumentSapMarkerTests
{
    [Fact]
    public void EtiquetaDeComentarios_EsCortaDeterministicaYSinCorchetes()
    {
        var a = DocumentSapMarkerService.CommentsTagFor("abc");

        Assert.Equal(a, DocumentSapMarkerService.CommentsTagFor("abc"));
        Assert.NotEqual(a, DocumentSapMarkerService.CommentsTagFor("abd"));
        Assert.Matches("^\\(BR-[0-9a-f]{12}\\)$", a);
    }

    [Fact]
    public void SinUdf_MarcaEnComentarios_YRecortaElTextoSiNoEntra()
    {
        var marker = new DocumentSapMarker(null, "k", "(BR-0123456789ab)");

        Assert.Equal("Urgente (BR-0123456789ab)", marker.ApplyToComments("Urgente"));
        Assert.Equal("(BR-0123456789ab)", marker.ApplyToComments(null));
        var largo = marker.ApplyToComments(new string('x', 400))!;
        Assert.True(largo.Length <= DocumentSapMarker.CommentsMaxLength);
        Assert.EndsWith("(BR-0123456789ab)", largo);
        Assert.Null(marker.ApplyToAdditionalFields(null));
    }

    [Fact]
    public void ConUdf_MarcaEnElCampo_YNoTocaComentarios()
    {
        var marker = new DocumentSapMarker("U_PortalDraftId", "abc123", "(BR-0123456789ab)");
        var campos = marker.ApplyToAdditionalFields(new Dictionary<string, object?> { ["U_Otro"] = 1 });

        Assert.Equal("Urgente", marker.ApplyToComments("Urgente"));
        Assert.Equal("abc123", campos!["U_PortalDraftId"]);
        Assert.Equal(1, campos["U_Otro"]);
    }

    private static async Task<(PortalSaasDbContext Db, Guid CompanyId)> CrearAsync()
    {
        var db = new PortalSaasDbContext(new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var company = new Company
        {
            OrganizationId = Guid.NewGuid(), Code = "C1", Name = "C", DatabaseName = "DB", ServiceLayerUrl = "https://sl",
            IntegrationUsername = "u", IntegrationSecretKey = "k", Country = "CL",
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return (db, company.Id);
    }

    private static DocumentDraft Enviando(Guid companyId, DateTimeOffset updated) => new()
    {
        CompanyId = companyId, Engine = DocumentDraftEngine.Inventory, DocumentType = "InventoryTransferRequest",
        Status = DocumentDraftStatus.Sending, Reason = "r", PayloadJson = "{}", CreatedByUserId = Guid.NewGuid(),
        CreatedByUsername = "ana", UpdatedByUsername = "ana", UpdatedAt = updated,
    };

    [Fact]
    public async Task Conciliacion_YaEnSap_PasaAEnviado_NoEnSap_PasaAConError_YAvisaAlCreador()
    {
        var (db, company) = await CrearAsync();
        var hace20 = DateTimeOffset.UtcNow.AddMinutes(-20);
        var creado = Enviando(company, hace20);
        var perdido = Enviando(company, hace20);
        var reciente = Enviando(company, DateTimeOffset.UtcNow.AddMinutes(-2));
        db.DocumentDrafts.AddRange(creado, perdido, reciente);
        await db.SaveChangesAsync();
        var marcador = new MarcadorFalso();
        marcador.EnSap[creado.Id.ToString("N")] = 777;
        var campana = new CampanaFalsaBorradores();

        var (sent, failed) = await DocumentDraftCleanupWorker.ReconcileCompanyAsync(db, marcador, campana, company, DateTimeOffset.UtcNow,
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal((1, 1), (sent, failed));
        Assert.Equal(DocumentDraftStatus.Sent, creado.Status);
        Assert.Equal(777, creado.SapDocEntry);
        Assert.Equal(DocumentDraftStatus.Error, perdido.Status);
        Assert.Equal(DocumentDraftStatus.Sending, reciente.Status); // < 10 min: puede seguir enviándose
        Assert.Equal(2, campana.Enviadas.Count);
    }

    [Fact]
    public async Task Conciliacion_ConSapCaido_NoTocaNada()
    {
        var (db, company) = await CrearAsync();
        var draft = Enviando(company, DateTimeOffset.UtcNow.AddMinutes(-20));
        db.DocumentDrafts.Add(draft);
        await db.SaveChangesAsync();

        var (sent, failed) = await DocumentDraftCleanupWorker.ReconcileCompanyAsync(db, new MarcadorFalso { SapCaido = true },
            new CampanaFalsaBorradores(), company, DateTimeOffset.UtcNow, NullLogger.Instance, CancellationToken.None);

        Assert.Equal((0, 0), (sent, failed));
        Assert.Equal(DocumentDraftStatus.Sending, draft.Status);
    }
}
