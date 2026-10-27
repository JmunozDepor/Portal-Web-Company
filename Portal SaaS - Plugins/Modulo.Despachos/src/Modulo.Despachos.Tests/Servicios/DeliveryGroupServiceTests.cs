using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Entregas con guía previa: candidatos por OC, agrupar (EG-), editar, condiciones y anular.</summary>
public class DeliveryGroupServiceTests
{
    internal static readonly DateOnly Desde = new(2026, 11, 1);
    internal const string Cliente = "C001";
    internal const string ClienteRut = "77261280-K";

    /// <summary>
    /// Cliente C001 (Falabella). Entregas 701 y 702 (OC 4500000001), 703 (OC 4500000002), 704 (dos OC) y 705 (OC "ABC").
    /// </summary>
    internal static ManifestTestEnv Env()
    {
        var env = ManifestTestEnv.Build();
        AddDelivery(env, 701, "4500000001");
        AddDelivery(env, 702, "4500000001");
        AddDelivery(env, 703, "4500000002");
        AddDelivery(env, 704, "4500000003", poCount: 2);
        AddDelivery(env, 705, "ABC");
        env.Sap.Partners["77261280-K"] = new SapBusinessPartner(Cliente, "Falabella", "Grandes tiendas", true, Array.Empty<SapPartnerAddress>());
        return env;
    }

    /// <summary>Entrega del fake SAP: DocNum = DocEntry + 1000, LPN "LPN" + DocEntry, fecha 09-11-2026, pedido 05-11-2026.</summary>
    internal static void AddDelivery(ManifestTestEnv env, int docEntry, string? po, int poCount = 1, string customer = Cliente,
        bool open = true, decimal units = 10, string commune = "Rancagua") =>
        env.Sap.Deliveries.Add(new SapDeliveryInfo(docEntry, docEntry + 1000, new DateOnly(2026, 11, 9), customer,
            customer == Cliente ? "Falabella" : "Otro cliente", customer == Cliente ? ClienteRut : "96520000-2",
            "Av. Kennedy 9001", commune, "LPN" + docEntry, po, new DateOnly(2026, 11, 5), poCount, units, units * 10000, open));

    internal static DeliveryGroupInput Input(params int[] docEntries) =>
        new(Cliente, Desde, ManifestTestEnv.Fecha, 2, 2, docEntries);

    [Fact]
    public async Task Candidatos_AgrupaPorOc_YBloqueaLasNoAgrupables()
    {
        var env = Env();

        var c = await env.Deliveries.ListCandidatesAsync(Cliente, Desde);

        Assert.Equal(("C001", "Falabella", ClienteRut), (c.CustomerCode, c.CustomerName, c.CustomerTaxId));
        Assert.Equal(new[] { 4500000001L, 4500000002L }, c.Orders.Select(o => o.CustomerPo));
        Assert.Equal(new[] { 701, 702 }, c.Orders[0].Deliveries.Select(d => d.DocEntry));
        Assert.Equal(new DateOnly(2026, 11, 5), c.Orders[0].OrderDate);
        Assert.Equal(new[] { 704, 705 }, c.Blocked.Select(b => b.Delivery.DocEntry));
        Assert.Equal("La Entrega tiene líneas de más de una OC; sepárela en SAP.", c.Blocked[0].Reason);
        Assert.Equal("La OC «ABC» no es numérica.", c.Blocked[1].Reason);
    }

    [Fact]
    public async Task Candidatos_SinOc_Bloqueada()
    {
        var env = Env();
        AddDelivery(env, 706, null);

        var c = await env.Deliveries.ListCandidatesAsync(Cliente, Desde);

        Assert.Equal("La Entrega no tiene orden de compra (pedido sin N° de referencia).",
            c.Blocked.Single(b => b.Delivery.DocEntry == 706).Reason);
    }

    [Fact]
    public async Task Candidatos_PorRut_ResuelveCardCode()
    {
        var env = Env();
        var c = await env.Deliveries.ListCandidatesAsync("77.261.280-K", Desde);
        Assert.Equal(2, c.Orders.Count);
    }

    [Fact]
    public async Task Candidatos_RutQueNoExiste_Lanza()
    {
        var env = Env();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Deliveries.ListCandidatesAsync("96520000-2", Desde));
        Assert.Equal("No se encontró el cliente 96520000-2 en SAP.", ex.Message);
    }

    [Fact]
    public async Task Generar_UnaAgrupacionPorOc_ConCondiciones()
    {
        var env = Env();

        var created = await env.Deliveries.GenerateAsync(Input(701, 702, 703));

        Assert.Equal(2, created.Count);
        Assert.All(created, g => Assert.Equal((DispatchGroupKinds.Delivery, 2, 2, Cliente, "Falabella", ClienteRut),
            (g.Kind, g.TransferIndicator!.Value, g.DispatchType!.Value, g.WarehouseCode, g.BranchName, g.CustomerTaxId)));
        Assert.Equal(new[] { 4500000001L, 4500000002L }, created.Select(g => g.CustomerPo!.Value));
        Assert.Equal(new[] { 1, 2 }, created.Select(g => g.Sequence));
        Assert.Equal(new DateOnly(2026, 11, 5), created[0].CustomerPoDate);
        Assert.Equal(new[] { 701, 702 }, created[0].Transfers.Select(t => t.SapDocEntry));
        Assert.StartsWith("EG-", Modulo.Despachos.Dominio.DispatchGroupCode.Format(created[0].Number, created[0].Kind));
    }

    [Theory]
    [InlineData(0, 2, "Indicador de traslado inválido.")]
    [InlineData(10, 2, "Indicador de traslado inválido.")]
    [InlineData(2, 4, "Tipo de despacho inválido.")]
    public async Task Generar_CondicionesFueraDeRango_Lanza(int indicator, int dispatchType, string message)
    {
        var env = Env();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            env.Deliveries.GenerateAsync(Input(701) with { TransferIndicator = indicator, DispatchType = dispatchType }));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task Generar_SoloBloqueadas_Lanza()
    {
        var env = Env();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Deliveries.GenerateAsync(Input(704, 705)));
        Assert.Equal("No hay Entregas libres para agrupar con la selección indicada.", ex.Message);
    }

    [Fact]
    public async Task Candidatos_ExcluyeOcupadas()
    {
        var env = Env();
        await env.Deliveries.GenerateAsync(Input(701));

        var c = await env.Deliveries.ListCandidatesAsync(Cliente, Desde);

        Assert.Equal(new[] { 702 }, c.Orders[0].Deliveries.Select(d => d.DocEntry));
    }

    [Fact]
    public async Task OcupacionDeTraslados_NoCuentaEntregasConMismoDocEntry()
    {
        var env = Env();
        await env.Master.SaveBranchWarehouseAsync(new BranchWarehouse { WarehouseCode = "TDA01", Name = "Tienda Uno", Address = "A 1", CommuneName = "Rancagua" });
        DispatchGroupServiceTests.AddTransfer(env, 701, "TDA01", "CTBO701", 5);
        await env.Deliveries.GenerateAsync(Input(701));

        var transfers = await env.Groups.ListCandidatesAsync(DispatchGroupServiceTests.Desde);
        Assert.Contains(701, transfers.SelectMany(x => x.Transfers).Select(t => t.DocEntry));

        // Y al revés: la transferencia 701 agrupada no ocupa la Entrega 702 ni la 701 de otra agrupación de entregas.
        await env.Groups.GenerateAsync(ManifestTestEnv.Fecha, DispatchGroupServiceTests.Desde, new[] { 701 });
        Assert.Equal(new[] { 702 }, (await env.Deliveries.ListCandidatesAsync(Cliente, Desde)).Orders[0].Deliveries.Select(d => d.DocEntry));
    }

    [Fact]
    public async Task AgregarLpn_MismaOc_Agrega()
    {
        var env = Env();
        var g = (await env.Deliveries.GenerateAsync(Input(701))).Single();

        var msg = await env.Deliveries.AddLpnAsync(g.Id, "lpn702");

        Assert.Contains("LPN702", msg);
        Assert.Equal(new[] { 701, 702 }, (await env.Groups.GetAsync(g.Id))!.Transfers.Select(t => t.SapDocEntry).OrderBy(x => x));
    }

    [Fact]
    public async Task AgregarLpn_OtraOc_Lanza()
    {
        var env = Env();
        var g = (await env.Deliveries.GenerateAsync(Input(701))).Single();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Deliveries.AddLpnAsync(g.Id, "LPN703"));

        Assert.Equal("El LPN LPN703 es de la OC 4500000002, no de la OC 4500000001 de esta agrupación.", ex.Message);
    }

    [Fact]
    public async Task AgregarLpn_OtroCliente_Lanza()
    {
        var env = Env();
        AddDelivery(env, 801, "4500000001", customer: "C999");
        var g = (await env.Deliveries.GenerateAsync(Input(701))).Single();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Deliveries.AddLpnAsync(g.Id, "LPN801"));

        Assert.Equal("El LPN LPN801 es de otro cliente.", ex.Message);
    }

    [Fact]
    public async Task QuitarEntrega_LaLibera()
    {
        var env = Env();
        var g = (await env.Deliveries.GenerateAsync(Input(701, 702))).Single();
        var line = g.Transfers.Single(t => t.SapDocEntry == 702);

        await env.Deliveries.RemoveDeliveryAsync(g.Id, line.Id);

        Assert.Equal(new[] { 702 }, (await env.Deliveries.ListCandidatesAsync(Cliente, Desde)).Orders[0].Deliveries.Select(d => d.DocEntry));
    }

    [Fact]
    public async Task CambiarCondiciones_Armada_LasGuarda()
    {
        var env = Env();
        var g = (await env.Deliveries.GenerateAsync(Input(701))).Single();

        await env.Deliveries.SetTermsAsync(g.Id, 1, 3);

        var r = (await env.Groups.GetAsync(g.Id))!;
        Assert.Equal((1, 3), (r.TransferIndicator!.Value, r.DispatchType!.Value));
    }

    [Fact]
    public async Task CambiarCondiciones_EnManifiesto_Lanza()
    {
        var env = Env();
        var g = (await env.Deliveries.GenerateAsync(Input(701))).Single();
        var row = env.Db.DispatchGroups.Single(x => x.Id == g.Id);
        row.Status = DispatchGroupStatuses.InManifest;
        await env.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => env.Deliveries.SetTermsAsync(g.Id, 1, 2));

        Assert.Contains("EG-", ex.Message);
        Assert.Contains("ya está en un manifiesto", ex.Message);
    }

    [Fact]
    public async Task Anular_LiberaEntregas()
    {
        var env = Env();
        var g = (await env.Deliveries.GenerateAsync(Input(701))).Single();

        await env.Deliveries.AnnulAsync(g.Id, "error");

        Assert.Equal(DispatchGroupStatuses.Annulled, (await env.Groups.GetAsync(g.Id))!.Status);
        Assert.Contains(701, (await env.Deliveries.ListCandidatesAsync(Cliente, Desde)).Orders[0].Deliveries.Select(d => d.DocEntry));
    }

    [Fact]
    public async Task ListaTraslados_NoMuestraEntregas_YViceversa()
    {
        var env = Env();
        await env.Deliveries.GenerateAsync(Input(701));

        Assert.Empty(await env.Groups.ListAsync(new DispatchGroupFilter(ManifestTestEnv.Fecha)));
        var deliveries = await env.Groups.ListAsync(new DispatchGroupFilter(ManifestTestEnv.Fecha, Kind: DispatchGroupKinds.Delivery));
        Assert.Equal("EG-000001", Assert.Single(deliveries).Code);
    }

    [Fact]
    public async Task AgruparTraslados_NoEditaAgrupacionDeEntregas()
    {
        var env = Env();
        var g = (await env.Deliveries.GenerateAsync(Input(701))).Single();

        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Groups.AnnulAsync(g.Id, "x"));
    }
}
