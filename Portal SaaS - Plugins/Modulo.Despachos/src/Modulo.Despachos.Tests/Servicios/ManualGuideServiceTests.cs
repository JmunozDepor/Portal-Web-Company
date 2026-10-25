using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Dominio.Guias;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Guías manuales: borrador GM-, motivos de traslado, emisión sola y la lista de todas las guías.</summary>
public class ManualGuideServiceTests
{
    internal static ManualGuideInput Input(ManifestTestEnv env, string rut = "17183714-6", int motive = 6, decimal price = 500000,
        string? notes = null, int dispatchType = 3, string mode = ManualTransportModes.Manual) =>
        new(ManifestTestEnv.Fecha, motive, dispatchType, env.OriginId,
            rut, "Freddi Fuentes Norambuena", "Tienda Paris Mall Plaza Maule", null,
            "Avenida Circunvalación 1055", "Rancagua", "Rancagua",
            "Avenida Circunvalación 1055", "Rancagua", "Rancagua",
            new[] { new ManualGuideLineInput(null, "Mueble", 1, null, price) },
            Array.Empty<ManualGuideReferenceInput>(), notes, mode,
            new ManualGuideTransportInput("ABCD12", null, "12345678-5", "Juan Pérez", "76543210-3", "TSM",
                ManifestTestEnv.Fecha, new TimeOnly(9, 0), ManifestTestEnv.Fecha));

    [Fact]
    public async Task Motivos_SemillaInicial()
    {
        var env = ManifestTestEnv.Build();
        var all = await env.ManualGuides.ListMotivesAsync(includeInactive: true);
        Assert.Equal(new[] { 2, 4, 5, 6, 7 }, all.Select(m => m.Code));
        var active = await env.ManualGuides.ListMotivesAsync();
        Assert.Equal(new[] { (5, false), (6, true) }, active.Select(m => (m.Code, m.VatAffected)));
    }

    [Fact]
    public async Task Crear_AsignaGm1_YCalculaTotales()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(Input(env));
        var g = (await env.ManualGuides.GetAsync(id))!;
        Assert.Equal((1, 595000m, 95000m, TransportGuideStatuses.Draft, TransportGuideSources.Manual),
            (g.Number, g.TotalAmount, g.VatAmount, g.Status, g.Source));
        Assert.Equal(("17183714-6", "Rancagua", 1, 1m), (g.ReceiverTaxId, g.DestinationCommune, g.LineCount, g.TotalQuantity));
    }

    [Fact]
    public async Task Crear_MotivoNoAfecto_SinIva()
    {
        var env = ManifestTestEnv.Build();
        var g = (await env.ManualGuides.GetAsync(await env.ManualGuides.CreateAsync(Input(env, motive: 5, price: 608))))!;
        Assert.Equal((608m, 0m, 608m), (g.NetAmount, g.VatAmount, g.TotalAmount));
    }

    [Fact]
    public async Task Crear_Segunda_Gm2()
    {
        var env = ManifestTestEnv.Build();
        await env.ManualGuides.CreateAsync(Input(env));
        var g = (await env.ManualGuides.GetAsync(await env.ManualGuides.CreateAsync(Input(env))))!;
        Assert.Equal(2, g.Number);
        Assert.Equal(2, (await env.ManualGuides.GetByNumberAsync(2))!.Number);
    }

    [Fact]
    public async Task Crear_Invalida_NoGuarda()
    {
        var env = ManifestTestEnv.Build();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.ManualGuides.CreateAsync(Input(env, rut: "12.345.678-0")));
        Assert.Contains("RUT del receptor", ex.Message);
        Assert.Equal(0, await env.Db.TransportGuides.CountAsync());
    }

    [Fact]
    public async Task Crear_MotivoInactivo_Rechaza()
    {
        var env = ManifestTestEnv.Build();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.ManualGuides.CreateAsync(Input(env, motive: 7)));
        Assert.Contains("motivo", ex.Message);
    }

    [Fact]
    public async Task Editar_YReabrir_ConservaLosDatos()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(Input(env));
        await env.ManualGuides.UpdateAsync(id, Input(env, notes: "Llamar antes", price: 100));
        var input = await env.ManualGuides.GetInputAsync(id);
        Assert.Equal(("Llamar antes", 100m, env.OriginId), (input.Notes, input.Lines[0].UnitPrice, input.OriginId));
    }

    [Fact]
    public async Task Emitir_Sola_FolioYObservaciones()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(Input(env, notes: "Entregar en bodega 38"));

        var g = await env.ManualGuides.EmitAsync(id);

        Assert.Equal((TransportGuideStatuses.Issued, 500L), (g.Status, g.Folio));
        var sent = Assert.Single(env.Dte.Calls).Document;
        Assert.Equal(ManualGuideBuilder.NotesLineName, sent.Lines[^1].Name);
        Assert.Equal("ABCD12", sent.Transport!.Plate);
        Assert.NotNull(g.IssuedAt);
        Assert.NotEmpty(await env.ManualGuides.GetPdfAsync(id));
    }

    [Fact]
    public async Task Emitir_DosVeces_UnSoloFolio()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(Input(env));
        await env.ManualGuides.EmitAsync(id);
        var again = await env.ManualGuides.EmitAsync(id);
        Assert.Equal(500L, again.Folio);
        Assert.Single(env.Dte.Calls);
    }

    [Fact]
    public async Task Emitir_ProveedorCaido_QuedaPendiente_YReintentoUsaLaMismaLlave()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(Input(env));
        env.Dte.Down = true;
        Assert.Equal(TransportGuideStatuses.Pending, (await env.ManualGuides.EmitAsync(id)).Status);
        env.Dte.Down = false;
        var g = await env.ManualGuides.EmitAsync(id);
        Assert.Equal(TransportGuideStatuses.Issued, g.Status);
        Assert.Single(env.Dte.Calls.Select(c => c.EmissionKey).Distinct());
    }

    [Fact]
    public async Task Emitir_Rechazada_LuegoCorregidaYReemitida()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(Input(env));
        env.Dte.RejectNext = "Giro inválido";
        var failed = await env.ManualGuides.EmitAsync(id);
        Assert.Equal((TransportGuideStatuses.Failed, "Giro inválido"), (failed.Status, failed.LastError));
        Assert.Contains(env.Notifications.Enqueued, n => n.Title.Contains("GM-000001"));

        await env.ManualGuides.UpdateAsync(id, Input(env, price: 400000));
        var g = await env.ManualGuides.EmitAsync(id);
        Assert.Equal(TransportGuideStatuses.Issued, g.Status);
    }

    [Fact]
    public async Task Crear_PorCuentaDelReceptor_SinTransporte_Rechaza()
    {
        var env = ManifestTestEnv.Build();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.ManualGuides.CreateAsync(Input(env, dispatchType: 1) with
        {
            Transport = new ManualGuideTransportInput(null, null, null, null, null, null),
        }));
        Assert.Contains("patente", ex.Message);
    }

    [Fact]
    public async Task Emitir_Sola_EnviaSalidaYLlegada()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(Input(env));
        await env.ManualGuides.EmitAsync(id);
        var t = Assert.Single(env.Dte.Calls).Document.Transport!;
        Assert.Equal((ManifestTestEnv.Fecha, new TimeOnly(9, 0), ManifestTestEnv.Fecha), (t.DepartureDate, t.DepartureTime, t.ArrivalDate));
    }

    [Fact]
    public void Motivo_EtiquetaDelSii() =>
        Assert.Equal(("5 - Traslados internos", "6 - Otros traslados no venta", "1 - Operación constituye venta"),
            (TransferIndicators.Label(5), TransferIndicators.Label(6), TransferIndicators.Label(1)));

    [Fact]
    public async Task Anular_SoloBorrador()
    {
        var env = ManifestTestEnv.Build();
        var issued = await env.ManualGuides.CreateAsync(Input(env));
        await env.ManualGuides.EmitAsync(issued);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.ManualGuides.AnnulAsync(issued, "error"));

        var draft = await env.ManualGuides.CreateAsync(Input(env));
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.ManualGuides.AnnulAsync(draft, " "));
        await env.ManualGuides.AnnulAsync(draft, "duplicada");
        var g = (await env.ManualGuides.GetAsync(draft))!;
        Assert.Equal((TransportGuideStatuses.Annulled, "duplicada"), (g.Status, g.AnnulReason));
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.ManualGuides.EmitAsync(draft));
    }

    [Fact]
    public async Task Lista_IncluyeManifiestoYManual()
    {
        var env = ManifestTestEnv.Build();
        await env.EnableGuidesAsync();
        var manifestId = await env.GuideReadyDraftAsync(901);
        Assert.True((await env.Svc.DispatchAsync(manifestId)).Success);
        await env.ManualGuides.CreateAsync(Input(env));

        var all = await env.ManualGuides.ListAsync(new GuideFilter(null, null, null, null, null));
        Assert.Equal(2, all.Count);
        Assert.Contains(all, x => x.Source == TransportGuideSources.Manifest && x.ManifestNumber == 1 && x.Folio == 500);
        Assert.Single(await env.ManualGuides.ListAsync(new GuideFilter(TransportGuideSources.Manual, null, null, null, null)));
        Assert.Single(await env.ManualGuides.ListAsync(new GuideFilter(null, null, null, null, "freddi")));
        Assert.Equal("GM-000001", (await env.ManualGuides.ListAsync(new GuideFilter(null, TransportGuideStatuses.Draft, null, null, null)))[0].Code);
    }

    [Fact]
    public async Task Receptor_DelegaEnSap()
    {
        var env = ManifestTestEnv.Build();
        env.Sap.Partners["76030680-0"] = new SapBusinessPartner("C76030680-0", "COMERCIAL DEPOR LIMITADA", "Venta", true, Array.Empty<SapPartnerAddress>());
        Assert.Equal("C76030680-0", (await env.ManualGuides.FindReceiverAsync("76.030.680-0"))!.CardCode);
    }
}
