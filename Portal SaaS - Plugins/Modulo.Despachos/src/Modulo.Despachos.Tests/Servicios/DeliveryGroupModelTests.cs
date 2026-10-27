using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Models;
using Modulo.Despachos.Tests.Fakes;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Agrupación de Entregas con guía previa: misma tabla que Agrupar traslados, con kind y datos de la guía.</summary>
public class DeliveryGroupModelTests
{
    [Fact]
    public async Task AgrupacionEntrega_SeGuarda()
    {
        var company = Guid.NewGuid();
        var name = "delivery-model-" + company;
        await using (var db = TestDb.Create(name))
        {
            db.DispatchGroups.Add(new DispatchGroup
            {
                CompanyId = company, Number = 1, Kind = DispatchGroupKinds.Delivery, WarehouseCode = "C76030680",
                BranchName = "Falabella", CustomerTaxId = "76030680-0", CustomerPo = 4500123456,
                CustomerPoDate = new DateOnly(2026, 10, 20), TransferIndicator = 2, DispatchType = 2,
                DispatchDate = new DateOnly(2026, 10, 27), Sequence = 1,
            });
            await db.SaveChangesAsync();
        }
        await using var read = TestDb.Create(name);
        var g = await read.DispatchGroups.SingleAsync(x => x.CompanyId == company);
        Assert.Equal((DispatchGroupKinds.Delivery, "C76030680", 4500123456L, 2, 2),
            (g.Kind, g.WarehouseCode, g.CustomerPo!.Value, g.TransferIndicator!.Value, g.DispatchType!.Value));
        Assert.Equal(new DateOnly(2026, 10, 20), g.CustomerPoDate);
    }

    [Fact]
    public void AgrupacionNueva_EsTraslado() => Assert.Equal(DispatchGroupKinds.Transfer, new DispatchGroup().Kind);
}
