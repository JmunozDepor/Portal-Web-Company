using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Entregas con guía previa en el manifiesto: hoja EG-, LPN de Entregas y control por DocEntry.</summary>
public class ManifestDeliveryGroupTests
{
    /// <summary>Agrupación EG-000001 (OC 4500000001: Entregas 701 y 702) y un borrador de manifiesto.</summary>
    internal static async Task<(ManifestTestEnv Env, DispatchGroup Group, Guid ManifestId)> ArrangeAsync()
    {
        var env = DeliveryGroupServiceTests.Env();
        var g = Assert.Single(await env.Deliveries.GenerateAsync(DeliveryGroupServiceTests.Input(701, 702)));
        var id = await env.Svc.CreateDraftAsync(env.Header());
        return (env, g, id);
    }

    private static Task<AddDocumentResult> Scan(ManifestTestEnv env, Guid id, string text) =>
        env.Svc.AddDocumentAsync(id, new DocumentCapture(text, null, null));

    private static async Task<string> GroupStatus(ManifestTestEnv env, long groupId) =>
        (await env.Db.DispatchGroups.AsNoTracking().SingleAsync(g => g.Id == groupId)).Status;

    [Fact]
    public async Task HojaEG_AgregaSusEntregas_ConClienteYOc()
    {
        var (env, g, id) = await ArrangeAsync();

        var r = await Scan(env, id, "EG-000001");

        Assert.Equal(AddDocumentOutcomes.GroupAdded, r.Outcome);
        Assert.Equal("EG-000001: 2 entregas agregadas.", r.Warnings[0]);
        var docs = (await env.Svc.GetAsync(id))!.Documents.OrderBy(d => d.SapDocEntry).ToList();
        Assert.Equal(new[] { 701, 702 }, docs.Select(d => d.SapDocEntry));
        var d = docs[0];
        Assert.Equal((DocumentKinds.Delivery, 0, 1701L, "LPN701", "C001", "Falabella", "77261280-K"),
            (d.DocumentKind, d.SiiDocumentType, d.Folio, d.Lpn, d.CustomerCode, d.CustomerName, d.CustomerTaxId));
        Assert.Equal(("Av. Kennedy 9001", env.RancaguaId, "4500000001", 100000m, 10m, 1, g.Id),
            (d.DestinationAddress, d.DestinationCommuneId!.Value, d.CustomerPo, d.TotalAmount, d.Units, d.Packages, d.DispatchGroupId!.Value));
        Assert.Equal(DispatchGroupStatuses.InManifest, await GroupStatus(env, g.Id));
    }

    [Fact]
    public async Task HojaEG_EntregaFacturadaEntretanto_SeInformaYElRestoEntra()
    {
        var (env, _, id) = await ArrangeAsync();
        var i = env.Sap.Deliveries.FindIndex(x => x.DocEntry == 702);
        env.Sap.Deliveries[i] = env.Sap.Deliveries[i] with { IsOpen = false };

        var r = await Scan(env, id, "EG-000001");

        Assert.Equal(AddDocumentOutcomes.GroupAdded, r.Outcome);
        Assert.Equal(701, Assert.Single((await env.Svc.GetAsync(id))!.Documents).SapDocEntry);
        Assert.Contains("La Entrega 1702 ya no está abierta en SAP.", r.Warnings);
    }

    [Fact]
    public async Task HojaAT_ConNumeroDeEntregas_NoExiste()
    {
        var (env, _, id) = await ArrangeAsync();

        var r = await Scan(env, id, "AT-000001");

        Assert.Equal(AddDocumentOutcomes.NotFound, r.Outcome);
        Assert.Empty((await env.Svc.GetAsync(id))!.Documents);
    }

    [Fact]
    public async Task LpnEntregaAgrupada_PideLaHoja()
    {
        var (env, _, id) = await ArrangeAsync();

        var r = await Scan(env, id, "LPN701");

        Assert.Equal(AddDocumentOutcomes.InDispatchGroup, r.Outcome);
        Assert.Equal("El LPN LPN701 pertenece a la agrupación EG-000001; escanee la hoja.", r.Warnings[0]);
    }

    [Fact]
    public async Task LpnEntregaSuelta_SeRechaza()
    {
        var (env, _, id) = await ArrangeAsync();

        var r = await Scan(env, id, "LPN703");

        Assert.Equal(AddDocumentOutcomes.NotInDeliveryGroup, r.Outcome);
        Assert.Equal("La Entrega 1703 (LPN LPN703) no está en una agrupación con guía previa; se despacha con su factura.", r.Warnings[0]);
    }

    [Fact]
    public async Task LpnSinConsultaDeEntregas_NoEncontrado()
    {
        var (env, _, id) = await ArrangeAsync();
        env.Sap.DeliveryByLpnMissing = true;

        var r = await Scan(env, id, "LPN703");

        Assert.Equal(AddDocumentOutcomes.NotFound, r.Outcome);
        Assert.Equal("No se encontró el LPN LPN703 en SAP.", r.Warnings[0]);
    }

    [Fact]
    public async Task EntregaEnOtroManifiesto_SeRechaza()
    {
        var (env, _, id) = await ArrangeAsync();
        await Scan(env, id, "EG-000001");
        var other = await env.Svc.CreateDraftAsync(env.Header());

        var r = await Scan(env, other, "EG-000001");

        Assert.Equal(AddDocumentOutcomes.InOtherManifest, r.Outcome);
        Assert.Empty((await env.Svc.GetAsync(other))!.Documents);
    }

    [Fact]
    public async Task TransferenciaYEntregaConMismoDocEntry_NoChocan()
    {
        var (env, _, id) = await ArrangeAsync();
        await env.Master.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = "TDA01", Name = "Tienda Uno", Address = "A 1", CommuneName = "Rancagua" });
        DispatchGroupServiceTests.AddTransfer(env, 701, "TDA01", "CTBO701", 5);
        await Scan(env, id, "EG-000001");

        // La transferencia 701 suelta entra: la Entrega 701 agrupada y en este manifiesto es otro documento.
        var r = await Scan(env, id, "CTBO701");

        Assert.Equal(AddDocumentOutcomes.Added, r.Outcome);
        Assert.Equal(3, (await env.Svc.GetAsync(id))!.Documents.Count);
    }

    [Fact]
    public async Task QuitarHojaEG_AgrupacionVuelveAArmada()
    {
        var (env, g, id) = await ArrangeAsync();
        await Scan(env, id, "EG-000001");

        await env.Svc.RemoveDispatchGroupAsync(id, g.Id);

        Assert.Equal(DispatchGroupStatuses.Armed, await GroupStatus(env, g.Id));
        Assert.Empty((await env.Svc.GetAsync(id))!.Documents);
    }
}
