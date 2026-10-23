using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Folio de la guía en la transferencia SAP (Fase B): después de emitir, reintento y "Probar escritura".</summary>
public class SapWritebackTests
{
    /// <summary>Guías habilitadas, ajustes de escritura, una sucursal y un borrador con la transferencia 501 (LPN CTBO1).</summary>
    private static async Task<(ManifestTestEnv Env, Guid Id)> TransferDraftAsync(string? prefix = "52", string? manifestField = "U_NX_Manifiesto")
    {
        var env = ManifestTestEnv.Build();
        await env.EnableGuidesAsync();
        await env.Master.SaveTransferSettingsAsync(null, prefix, manifestField, null, 5, 3);
        await env.Master.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = "TDA01", Name = "Tienda", Address = "Estado 387", CommuneName = "Rancagua" });
        env.Sap.Transfers.Add(new SapTransferInfo(501, 1501, ManifestTestEnv.Fecha, "TDA01", "Tienda", null, null, 2m, false, "CTBO1"));
        env.Sap.Lines[501] = new SapDocumentLines(null, "Rancagua", new[] { new SapDocumentLine(0, "A", "Zapatilla", 2, "UN", 1m) });
        var id = await env.Svc.CreateDraftAsync(env.Header(arrival: ManifestTestEnv.Fecha));
        Assert.Equal("added", (await env.Svc.AddDocumentAsync(id, new DocumentCapture("CTBO1", null, null))).Outcome);
        return (env, id);
    }

    private static async Task<ManifestDocument> TransferDoc(ManifestTestEnv env, Guid id) =>
        await env.Db.ManifestDocuments.AsNoTracking().SingleAsync(d => d.ManifestId == id && d.DocumentKind == DocumentKinds.Transfer);

    [Fact]
    public async Task Emitir_Traslado_EscribeFolioPrefijoYCampos()
    {
        var (env, id) = await TransferDraftAsync();
        env.Dte.NextFolio = 107;

        Assert.True((await env.Svc.DispatchAsync(id)).Success);

        var (docEntry, fields) = Assert.Single(env.Writer.Writes);
        Assert.Equal(501, docEntry);
        Assert.Equal(107L, fields["FolioNumber"]);
        Assert.Equal("52", fields["FolioPrefixString"]);
        Assert.Equal((await env.Svc.GetAsync(id))!.Number, fields["U_NX_Manifiesto"]);
        Assert.Equal(SapWritebackStatuses.Ok, (await TransferDoc(env, id)).SapWritebackStatus);
    }

    [Fact]
    public async Task Emitir_Traslado_FallaSap_GuiaEmitida_DocumentoPendiente_YAviso()
    {
        var (env, id) = await TransferDraftAsync();
        env.Writer.FailWith = "Documento cerrado: no se puede actualizar";

        Assert.True((await env.Svc.DispatchAsync(id)).Success);

        Assert.Equal(ManifestStatuses.Dispatched, (await env.Svc.GetAsync(id))!.Status);
        Assert.Equal(TransportGuideStatuses.Issued, (await env.Db.TransportGuides.SingleAsync(g => g.ManifestId == id)).Status);
        var doc = await TransferDoc(env, id);
        Assert.Equal(SapWritebackStatuses.Pending, doc.SapWritebackStatus);
        Assert.Contains("Documento cerrado", doc.SapWritebackError);
        Assert.Contains(env.Notifications.Enqueued, n => n.Message.Contains("transferencia 1501") && n.Url!.Contains("tab=guias"));
        Assert.Contains(env.Db.ManifestEvents, e => e.ManifestId == id && e.EventType == ManifestEventTypes.SapWritebackFailed);
    }

    [Fact]
    public async Task ReintentarSap_EscribeLosPendientes_SinReemitir()
    {
        var (env, id) = await TransferDraftAsync();
        env.Writer.FailWith = "SAP no disponible";
        await env.Svc.DispatchAsync(id);
        var emissions = env.Dte.Calls.Count;
        env.Writer.FailWith = null;

        var pending = await env.Guides.RetrySapWritebackAsync(id);

        Assert.Equal(0, pending);
        Assert.Single(env.Writer.Writes);
        Assert.Equal(emissions, env.Dte.Calls.Count);
        var doc = await TransferDoc(env, id);
        Assert.Equal((SapWritebackStatuses.Ok, (string?)null), (doc.SapWritebackStatus, doc.SapWritebackError));
    }

    [Fact]
    public async Task Emitir_Traslado_SinPrefijoNiCampos_NoLlamaSap_EstadoNulo()
    {
        var (env, id) = await TransferDraftAsync(prefix: "", manifestField: null);

        Assert.True((await env.Svc.DispatchAsync(id)).Success);

        Assert.Empty(env.Writer.Writes);
        Assert.Null((await TransferDoc(env, id)).SapWritebackStatus);
    }

    [Fact]
    public async Task Emitir_Factura_NoEscribeEnSap()
    {
        var env = ManifestTestEnv.Build();
        await env.EnableGuidesAsync();
        var id = await env.GuideReadyDraftAsync(1);

        Assert.True((await env.Svc.DispatchAsync(id)).Success);

        Assert.Empty(env.Writer.Writes);
    }

    [Fact]
    public async Task ProbarEscritura_ReescribeValoresActuales()
    {
        var (env, _) = await TransferDraftAsync();
        env.Writer.Current[777] = new Dictionary<string, object?> { ["FolioNumber"] = 55L, ["FolioPrefixString"] = "52", ["U_NX_Manifiesto"] = null };

        var message = await env.Guides.TestSapWriteAsync(777);

        Assert.Contains("777", message);
        var (docEntry, fields) = Assert.Single(env.Writer.Writes);
        Assert.Equal(777, docEntry);
        Assert.Equal(55L, fields["FolioNumber"]);
        Assert.Equal("52", fields["FolioPrefixString"]);
        Assert.True(fields.ContainsKey("U_NX_Manifiesto"));
    }

    [Fact]
    public async Task ProbarEscritura_SapRechaza_MensajeClaro()
    {
        var (env, _) = await TransferDraftAsync();
        env.Writer.FailWith = "No se puede modificar un documento cerrado";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Guides.TestSapWriteAsync(777));

        Assert.Contains("SAP rechazó la escritura en la transferencia 777", ex.Message);
        Assert.Contains("documento cerrado", ex.Message);
    }

    [Fact]
    public async Task ProbarEscritura_SinCamposConfigurados_Lanza()
    {
        var (env, _) = await TransferDraftAsync(prefix: "", manifestField: null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Guides.TestSapWriteAsync(777));
        Assert.Contains("No hay campos configurados", ex.Message);
    }
}
