using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Agrupaciones: candidatos por sucursal, generar (corte por horario), editar y anular.</summary>
public class DispatchGroupServiceTests
{
    internal static readonly DateOnly Desde = new(2026, 11, 1);

    /// <summary>Sucursales TDA01 y TDA02 activas; BOD99 no es sucursal. Transferencias 501, 502 (TDA01), 503 (TDA02), 504 (BOD99).</summary>
    internal static async Task<ManifestTestEnv> EnvAsync()
    {
        var env = ManifestTestEnv.Build();
        await env.Master.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = "TDA01", Name = "Tienda Uno", Address = "A 1", CommuneName = "Rancagua" });
        await env.Master.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = "TDA02", Name = "Tienda Dos", Address = "B 2", CommuneName = "Rancagua" });
        AddTransfer(env, 501, "TDA01", "CTBO501", 8);
        AddTransfer(env, 502, "TDA01", "CTBO502", 9);
        AddTransfer(env, 503, "TDA02", "CTBO503", 6);
        AddTransfer(env, 504, "BOD99", "CTBO504", 3);
        return env;
    }

    /// <summary>Transferencia del fake SAP con DocNum = DocEntry + 10000 y fecha 09-11-2026.</summary>
    internal static void AddTransfer(ManifestTestEnv env, int docEntry, string warehouse, string? lpn, decimal units, bool canceled = false) =>
        env.Sap.Transfers.Add(new SapTransferInfo(docEntry, docEntry + 10000, new DateOnly(2026, 11, 9), warehouse,
            "Bodega " + warehouse, null, null, units, canceled, lpn));

    [Fact]
    public async Task Candidatos_SoloSucursalesActivas_AgrupadasPorBodega()
    {
        var env = await EnvAsync();

        var c = await env.Groups.ListCandidatesAsync(Desde);

        Assert.Equal(new[] { "TDA01", "TDA02" }, c.Select(x => x.Branch.WarehouseCode));
        Assert.Equal(new[] { 501, 502 }, c[0].Transfers.Select(t => t.DocEntry));
    }

    [Fact]
    public async Task Candidatos_FiltroPorSucursal()
    {
        var env = await EnvAsync();
        var c = await env.Groups.ListCandidatesAsync(Desde, "TDA02");
        Assert.Equal("TDA02", Assert.Single(c).Branch.WarehouseCode);
    }

    [Fact]
    public async Task Candidatos_ExcluyeOcupadas()
    {
        var env = await EnvAsync();
        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501 });
        var id = await env.Svc.CreateDraftAsync(env.Header());
        Assert.Equal("added", (await env.Svc.AddDocumentAsync(id, new DocumentCapture("CTBO503", null, null))).Outcome);

        var c = await env.Groups.ListCandidatesAsync(Desde);

        Assert.Equal(new[] { 502 }, c.SelectMany(x => x.Transfers).Select(t => t.DocEntry));
    }

    [Fact]
    public async Task Generar_UnaAgrupacionPorSucursal_ConFotoDeDatos()
    {
        var env = await EnvAsync();

        var groups = await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501, 502, 503 });

        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { 1, 2 }, groups.Select(g => g.Number));
        Assert.All(groups, g => Assert.Equal((DispatchGroupStatuses.Armed, 1, ManifestTestEnv.Fecha), (g.Status, g.Sequence, g.DispatchDate)));
        var g1 = await env.Groups.GetAsync(groups[0].Id);
        Assert.Equal(("TDA01", "Tienda Uno"), (g1!.WarehouseCode, g1.BranchName));
        Assert.Equal(new[] { ("CTBO501", 8m, 10501), ("CTBO502", 9m, 10502) },
            g1.Transfers.OrderBy(t => t.SapDocEntry).Select(t => (t.Lpn!, t.Units, t.SapDocNum)));
    }

    [Fact]
    public async Task Generar_DeNuevoElMismoDia_SoloNuevas_Grupo2()
    {
        var env = await EnvAsync();
        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501, 502, 503 });
        AddTransfer(env, 505, "TDA01", "CTBO505", 4);

        var c = await env.Groups.ListCandidatesAsync(Desde);
        var groups = await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, c.SelectMany(x => x.Transfers).Select(t => t.DocEntry).ToList());

        var g = Assert.Single(groups);
        Assert.Equal((2, 3), (g.Sequence, g.Number));
        Assert.Equal(505, Assert.Single((await env.Groups.GetAsync(g.Id))!.Transfers).SapDocEntry);
    }

    [Fact]
    public async Task Generar_DocEntryNoElegidaNoEntra()
    {
        var env = await EnvAsync();
        var g = Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 502 }));
        Assert.Equal(502, Assert.Single((await env.Groups.GetAsync(g.Id))!.Transfers).SapDocEntry);
    }

    [Fact]
    public async Task Generar_SinNingunaLibre_Lanza()
    {
        var env = await EnvAsync();
        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501 });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501, 504 }));
        Assert.Contains("No hay transferencias", ex.Message);
    }

    [Fact]
    public async Task Generar_AvisaPorCampana()
    {
        var env = await EnvAsync();
        await env.Master.SaveNotificationRecipientAsync(new NotificationRecipient { UserId = Guid.NewGuid() });

        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501, 503 });

        var n = Assert.Single(env.Notifications.Enqueued);
        Assert.Contains("2 agrupaciones listas para el 10-11 (2 sucursales)", n.Message);
        Assert.Equal("/despachos/agrupar-traslados?dispatchDate=2026-11-10", n.Url);
    }

    [Fact]
    public async Task AgregarLpn_OtraSucursal_Rechaza()
    {
        var env = await EnvAsync();
        var g = Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501 }));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Groups.AddLpnAsync(g.Id, "CTBO503"));
        Assert.Contains("no a la sucursal de esta agrupación", ex.Message);
    }

    [Fact]
    public async Task AgregarLpn_EnOtraAgrupacion_Rechaza()
    {
        var env = await EnvAsync();
        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501 });
        var g2 = Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 502 }));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Groups.AddLpnAsync(g2.Id, "CTBO501"));
        Assert.Contains("AT-000001", ex.Message);
    }

    [Fact]
    public async Task AgregarLpn_Valido_SumaLinea()
    {
        var env = await EnvAsync();
        var g = Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501 }));

        var msg = await env.Groups.AddLpnAsync(g.Id, "ctbo502");

        Assert.Contains("CTBO502", msg);
        Assert.Equal(2, (await env.Groups.GetAsync(g.Id))!.Transfers.Count);
    }

    [Fact]
    public async Task Quitar_Y_Anular_SoloArmada()
    {
        var env = await EnvAsync();
        var g = Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501, 502 }));
        var row = await env.Db.DispatchGroups.SingleAsync(x => x.Id == g.Id);
        row.Status = DispatchGroupStatuses.InManifest;
        await env.Db.SaveChangesAsync();
        var line = (await env.Groups.GetAsync(g.Id))!.Transfers[0];

        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Groups.RemoveTransferAsync(g.Id, line.Id));
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Groups.AnnulAsync(g.Id, "error"));
        Assert.Contains("ya está en un manifiesto", ex1.Message);
        Assert.Contains("ya está en un manifiesto", ex2.Message);
    }

    [Fact]
    public async Task Quitar_Armada_SacaLaLinea()
    {
        var env = await EnvAsync();
        var g = Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501, 502 }));
        var line = (await env.Groups.GetAsync(g.Id))!.Transfers.Single(t => t.SapDocEntry == 501);

        await env.Groups.RemoveTransferAsync(g.Id, line.Id);

        Assert.Equal(502, Assert.Single((await env.Groups.GetAsync(g.Id))!.Transfers).SapDocEntry);
    }

    [Fact]
    public async Task Anular_LiberaParaCandidatos()
    {
        var env = await EnvAsync();
        var g = Assert.Single(await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501 }));

        await env.Groups.AnnulAsync(g.Id, "mal armada");

        Assert.Equal(DispatchGroupStatuses.Annulled, (await env.Groups.GetAsync(g.Id))!.Status);
        Assert.Contains(501, (await env.Groups.ListCandidatesAsync(Desde)).SelectMany(x => x.Transfers).Select(t => t.DocEntry));
    }

    [Fact]
    public async Task Listar_FiltraPorFecha_ConTotales()
    {
        var env = await EnvAsync();
        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501, 502 });
        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha.AddDays(1), Desde, new[] { 503 });

        var list = await env.Groups.ListAsync(new DispatchGroupFilter(ManifestTestEnv.Fecha));

        var only = Assert.Single(list);
        Assert.Equal(("AT-000001", "TDA01", 2, 17m), (only.Code, only.WarehouseCode, only.Transfers, only.Units));
    }

    [Fact]
    public async Task Listar_RangoDeFechas_IncluyeAmbosExtremos()
    {
        var env = await EnvAsync();
        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, Desde, new[] { 501 });
        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha.AddDays(2), Desde, new[] { 502 });
        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha.AddDays(3), Desde, new[] { 503 });

        var list = await env.Groups.ListAsync(new DispatchGroupFilter(From: ManifestTestEnv.Fecha, To: ManifestTestEnv.Fecha.AddDays(2)));

        Assert.Equal(new[] { "AT-000002", "AT-000001" }, list.Select(x => x.Code));
    }
}
