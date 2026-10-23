using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Transferencias a sucursales en el manifiesto (Fase B): por LPN escaneado y pendientes por sucursal.</summary>
public class ManifestTransfersTests
{
    private static async Task<ManifestTestEnv> EnvAsync(string? lpnPattern = null)
    {
        var env = ManifestTestEnv.Build();
        await env.Master.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = "TDA01", Name = "Tienda Estado", Address = "Estado 387", CommuneName = "Santiago" });
        await env.Master.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = "TDA02", Name = "Tienda Cerrada", IsActive = false });
        if (lpnPattern != null)
        {
            env.Db.Settings.Single(x => x.CompanyId == env.Company.CompanyId).LpnPattern = lpnPattern;
            await env.Db.SaveChangesAsync();
        }
        return env;
    }

    private static SapTransferInfo Transfer(int docEntry, string lpn, string warehouse = "TDA01", bool canceled = false) =>
        new(docEntry, docEntry + 1000, new DateOnly(2026, 11, 5), warehouse, "Bodega " + warehouse,
            "Dirección SAP 1", "Santiago", 24m, canceled, lpn);

    private static Task<AddDocumentResult> Scan(ManifestTestEnv env, Guid id, string text) =>
        env.Svc.AddDocumentAsync(id, new DocumentCapture(text, null, null));

    [Fact]
    public async Task Lpn_Escaneado_AgregaTransferenciaConDatosDeLaSucursal()
    {
        var env = await EnvAsync();
        env.Sap.Transfers.Add(Transfer(501, "CTBO123"));
        var id = await env.Svc.CreateDraftAsync(env.Header());

        var r = await Scan(env, id, " ctbo123 ");

        Assert.Equal(AddDocumentOutcomes.Added, r.Outcome);
        var d = Assert.Single((await env.Svc.GetAsync(id))!.Documents);
        Assert.Equal((DocumentKinds.Transfer, 0, 1501L, 501, "CTBO123"), (d.DocumentKind, d.SiiDocumentType, d.Folio, d.SapDocEntry, d.Lpn));
        Assert.Equal(("TDA01", "Tienda Estado", ManifestTestEnv.IssuerRut), (d.CustomerCode, d.CustomerName, d.CustomerTaxId));
        Assert.Equal(("Estado 387", "Santiago"), (d.DestinationAddress, d.DestinationCommuneName));
        Assert.NotNull(d.DestinationCommuneId);
        Assert.Equal((24m, 1, 0m), (d.Units, d.Packages, d.TotalAmount));
        Assert.Equal(CaptureMethods.Pdf417, d.CaptureMethod);
    }

    [Fact]
    public async Task Lpn_NoExiste_NotFound()
    {
        var env = await EnvAsync();
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var r = await Scan(env, id, "CTBO999");
        Assert.Equal(AddDocumentOutcomes.NotFound, r.Outcome);
        Assert.Contains("No se encontró el LPN CTBO999", r.Warnings[0]);
    }

    [Fact]
    public async Task Lpn_Anulada_Canceled()
    {
        var env = await EnvAsync();
        env.Sap.Transfers.Add(Transfer(501, "CTBO123", canceled: true));
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var r = await Scan(env, id, "CTBO123");
        Assert.Equal(AddDocumentOutcomes.Canceled, r.Outcome);
        Assert.Contains("La transferencia 1501 está anulada", r.Warnings[0]);
    }

    [Theory]
    [InlineData("BOD99")]
    [InlineData("TDA02")]
    public async Task Lpn_BodegaNoSucursal_Rechaza(string warehouse)
    {
        var env = await EnvAsync();
        env.Sap.Transfers.Add(Transfer(501, "CTBO123", warehouse));
        var id = await env.Svc.CreateDraftAsync(env.Header());

        var r = await Scan(env, id, "CTBO123");

        Assert.Equal(AddDocumentOutcomes.NotBranch, r.Outcome);
        Assert.Contains($"va a la bodega {warehouse}", r.Warnings[0]);
        Assert.Contains("no es sucursal", r.Warnings[0]);
        Assert.Empty((await env.Svc.GetAsync(id))!.Documents);
    }

    [Fact]
    public async Task Lpn_DosVecesEnElMismo_Duplicado()
    {
        var env = await EnvAsync();
        env.Sap.Transfers.Add(Transfer(501, "CTBO123"));
        var id = await env.Svc.CreateDraftAsync(env.Header());
        await Scan(env, id, "CTBO123");

        var r = await Scan(env, id, "CTBO123");

        Assert.Equal(AddDocumentOutcomes.DuplicateInThis, r.Outcome);
        Assert.Single((await env.Svc.GetAsync(id))!.Documents);
    }

    [Fact]
    public async Task Lpn_EnOtroManifiestoDespachado_InOtherManifest()
    {
        var env = await EnvAsync();
        env.Sap.Transfers.Add(Transfer(501, "CTBO123"));
        var first = await env.Svc.CreateDraftAsync(env.Header());
        await Scan(env, first, "CTBO123");
        Assert.True((await env.Svc.DispatchAsync(first)).Success);
        var number = (await env.Svc.GetAsync(first))!.Number;

        var second = await env.Svc.CreateDraftAsync(env.Header());
        var r = await Scan(env, second, "CTBO123");

        Assert.Equal(AddDocumentOutcomes.InOtherManifest, r.Outcome);
        Assert.Equal(number, r.OtherManifestNumber);
    }

    [Fact]
    public async Task Lpn_PatronConfigurado_TextoQueNoCalza_InvalidScan()
    {
        var env = await EnvAsync(lpnPattern: @"^(CTBO|ICBO)\d+$");
        env.Sap.Transfers.Add(Transfer(501, "XX123"));
        var id = await env.Svc.CreateDraftAsync(env.Header());

        Assert.Equal(AddDocumentOutcomes.InvalidScan, (await Scan(env, id, "XX123")).Outcome);
        env.Sap.Transfers.Add(Transfer(502, "ICBO7"));
        Assert.Equal(AddDocumentOutcomes.Added, (await Scan(env, id, "icbo7")).Outcome);
    }

    [Fact]
    public async Task Pendientes_AgregaLasQuePasan_YReportaLasSaltadas()
    {
        var env = await EnvAsync();
        env.Sap.Transfers.Add(Transfer(501, "CTBO1"));
        env.Sap.Transfers.Add(Transfer(502, "CTBO2"));
        env.Sap.Transfers.Add(Transfer(503, "CTBO3"));
        var other = await env.Svc.CreateDraftAsync(env.Header());
        await Scan(env, other, "CTBO2");
        var id = await env.Svc.CreateDraftAsync(env.Header());
        await Scan(env, id, "CTBO3");

        var r = await env.Svc.AddPendingTransfersAsync(id, "TDA01", new DateOnly(2026, 11, 1));

        Assert.Equal(1, r.Added);
        Assert.Equal(2, r.Skipped.Count);
        Assert.Contains(r.Skipped, s => s.StartsWith("1502:"));
        Assert.Contains(r.Skipped, s => s.StartsWith("1503:"));
        var docs = (await env.Svc.GetAsync(id))!.Documents;
        Assert.Equal(new[] { 501, 503 }, docs.Select(d => d.SapDocEntry).OrderBy(x => x));
        Assert.Contains(docs, d => d.SapDocEntry == 501 && d.CaptureMethod == CaptureMethods.Manual);
    }

    [Theory]
    [InlineData("BOD99")]
    [InlineData("TDA02")]
    public async Task Pendientes_BodegaNoSucursal_Lanza(string warehouse)
    {
        var env = await EnvAsync();
        var id = await env.Svc.CreateDraftAsync(env.Header());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Svc.AddPendingTransfersAsync(id, warehouse, new DateOnly(2026, 11, 1)));
    }
}
