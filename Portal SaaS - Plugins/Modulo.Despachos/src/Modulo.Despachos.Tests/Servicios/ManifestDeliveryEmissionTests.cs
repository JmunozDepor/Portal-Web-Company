using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Entregas con guía previa al despachar: guía valorizada por OC aunque las guías Res. 154 estén apagadas, y folio en la Entrega.</summary>
public class ManifestDeliveryEmissionTests
{
    /// <summary>Hoja EG-000001 (701 y 702) escaneada en un borrador con fecha de llegada; GuidesEnabled queda en false.</summary>
    private static async Task<(ManifestTestEnv Env, Guid Id, DispatchGroup Group)> ArrangeAsync()
    {
        var env = DeliveryGroupServiceTests.Env();
        var g = Assert.Single(await env.Deliveries.GenerateAsync(DeliveryGroupServiceTests.Input(701, 702)));
        foreach (var e in new[] { 701, 702 })
            env.Sap.Lines[e] = new SapDocumentLines("Grandes tiendas", "Santiago",
                new[] { new SapDocumentLine(0, "SKU" + e, "Zapatilla " + e, 2, "UN", 1m, Amount: 20000m) });
        var id = await env.Svc.CreateDraftAsync(env.Header(arrival: ManifestTestEnv.Fecha));
        Assert.Equal(AddDocumentOutcomes.GroupAdded, (await env.Svc.AddDocumentAsync(id, new DocumentCapture("EG-000001", null, null))).Outcome);
        return (env, id, g);
    }

    [Fact]
    public async Task Despachar_SoloEntregas_GuiasApagadas_EmiteGuiaValorizada()
    {
        var (env, id, _) = await ArrangeAsync();

        var r = await env.Svc.DispatchAsync(id);

        Assert.True(r.Success, string.Join(" ", r.Errors));
        var call = Assert.Single(env.Dte.Calls);
        Assert.Equal((2, 2), (call.Document.IdDoc.TransferIndicator!.Value, call.Document.IdDoc.DispatchType!.Value));
        Assert.Equal("801", Assert.Single(call.Document.References).DocumentType);
        Assert.All(call.Document.Lines, l => Assert.Equal(10000m, l.UnitPrice));
        Assert.Equal(ManifestStatuses.Dispatched, (await env.Svc.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Emitida_EscribeFolioEnLaEntrega()
    {
        var (env, id, _) = await ArrangeAsync();
        env.Dte.NextFolio = 321;

        Assert.True((await env.Svc.DispatchAsync(id)).Success);

        Assert.Empty(env.Writer.Writes);
        Assert.Equal(new[] { 701, 702 }, env.Writer.DeliveryWrites.Select(w => w.DocEntry).OrderBy(x => x));
        Assert.All(env.Writer.DeliveryWrites, w =>
        {
            Assert.Equal(321L, w.Fields["FolioNumber"]);
            Assert.Equal("52", w.Fields["FolioPrefixString"]);
            Assert.Equal(2, w.Fields.Count);
        });
        Assert.All(await env.Db.ManifestDocuments.AsNoTracking().Where(d => d.ManifestId == id).ToListAsync(),
            d => Assert.Equal(SapWritebackStatuses.Ok, d.SapWritebackStatus));
    }

    [Fact]
    public async Task EscrituraEntregaFalla_SapPendienteYAviso()
    {
        var (env, id, _) = await ArrangeAsync();
        env.Writer.FailWith = "Documento cerrado";

        Assert.True((await env.Svc.DispatchAsync(id)).Success);

        var docs = await env.Db.ManifestDocuments.AsNoTracking().Where(d => d.ManifestId == id).ToListAsync();
        Assert.All(docs, d => Assert.Equal(SapWritebackStatuses.Pending, d.SapWritebackStatus));
        Assert.Contains(env.Notifications.Enqueued, n => n.Message.Contains("Entrega"));
    }

    [Fact]
    public async Task Despachado_AgrupacionPasaADespachada()
    {
        var (env, id, g) = await ArrangeAsync();

        Assert.True((await env.Svc.DispatchAsync(id)).Success);

        Assert.Equal(DispatchGroupStatuses.Dispatched, (await env.Db.DispatchGroups.AsNoTracking().SingleAsync(x => x.Id == g.Id)).Status);
    }

    [Fact]
    public async Task ProbarEscrituraEntrega_ReescribeValoresActuales()
    {
        var (env, _, _) = await ArrangeAsync();
        env.Writer.DeliveryCurrent[701] = new Dictionary<string, object?> { ["FolioNumber"] = 0L, ["FolioPrefixString"] = null };

        var msg = await env.Guides.TestSapDeliveryWriteAsync(701);

        Assert.Equal("SAP aceptó la escritura en la Entrega 701.", msg);
        Assert.Equal(701, Assert.Single(env.Writer.DeliveryWrites).DocEntry);
    }
}
