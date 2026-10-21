using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;

namespace Modulo.Despachos.Tests.Servicios;

public class MasterDataServiceTests
{
    private static (MasterDataService Svc, DespachosDbContext Db, FakeCurrentCompanyAccessor Company) Build(
        DespachosDbContext? db = null, FakeCurrentCompanyAccessor? company = null)
    {
        db ??= TestDb.Create();
        company ??= new FakeCurrentCompanyAccessor();
        return (new MasterDataService(db, company, new FakeCurrentUserContext()), db, company);
    }

    [Fact]
    public async Task SaveCarrier_RechazaRutInvalido_SalvoRetiro()
    {
        var (svc, _, _) = Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveCarrierAsync(
            new Carrier { Code = "TSM", Name = "TSM", Kind = CarrierKinds.ThirdParty, TaxId = "76086428-0" }));
        Assert.Contains("RUT", ex.Message);

        var id = await svc.SaveCarrierAsync(new Carrier { Code = "RETIRO", Name = "Retiro en tienda", Kind = CarrierKinds.Pickup });
        Assert.True(id > 0);
    }

    [Fact]
    public async Task SaveCarrier_ConRutValido_LoNormaliza()
    {
        var (svc, _, _) = Build();
        await svc.SaveCarrierAsync(new Carrier { Code = "TSM", Name = "TSM", Kind = CarrierKinds.ThirdParty, TaxId = "76.086.428-5" });
        Assert.Equal("76086428-5", (await svc.ListCarriersAsync()).Single().TaxId);
    }

    [Fact]
    public async Task SaveVehicle_NormalizaPatente()
    {
        var (svc, _, _) = Build();

        var id = await svc.SaveVehicleAsync(new Vehicle { Plate = "ab-cd 12" });
        Assert.Equal("ABCD12", (await svc.ListVehiclesAsync()).Single(v => v.Id == id).Plate);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveVehicleAsync(new Vehicle { Plate = "ABCD12" }));
        Assert.Contains("ya existe", ex.Message);
    }

    [Fact]
    public async Task SaveVehicle_Editar_ActualizaSinDuplicarse()
    {
        var (svc, _, _) = Build();
        var id = await svc.SaveVehicleAsync(new Vehicle { Plate = "ABCD12" });

        await svc.SaveVehicleAsync(new Vehicle { Id = id, Plate = "abcd12", IsTrailer = true });

        var v = Assert.Single(await svc.ListVehiclesAsync());
        Assert.True(v.IsTrailer);
        Assert.NotNull(v.UpdatedAt);
    }

    [Fact]
    public async Task SaveDriver_ValidaRutYNormaliza()
    {
        var (svc, _, _) = Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SaveDriverAsync(
            new Driver { TaxId = "12345678-0", FullName = "Juan" }));
        Assert.Contains("RUT", ex.Message);

        var id = await svc.SaveDriverAsync(new Driver { TaxId = "12.345.678-5", FullName = "Juan Pérez" });
        Assert.Equal("12345678-5", (await svc.ListDriversAsync()).Single(d => d.Id == id).TaxId);
    }

    [Fact]
    public async Task DeleteVehicleType_BloqueadoSiHayVehiculos()
    {
        var (svc, _, _) = Build();
        var typeId = await svc.SaveVehicleTypeAsync(new VehicleType { Code = "CAM", Name = "Camión" });
        await svc.SaveVehicleAsync(new Vehicle { Plate = "ABCD12", VehicleTypeId = typeId });

        var result = await svc.DeleteVehicleTypeAsync(typeId);

        Assert.False(result.Deleted);
        Assert.Contains("vehículos", result.BlockedReason);
        Assert.Single(await svc.ListVehicleTypesAsync());
    }

    [Fact]
    public async Task DeleteVehicleType_SinVehiculos_Borra()
    {
        var (svc, _, _) = Build();
        var typeId = await svc.SaveVehicleTypeAsync(new VehicleType { Code = "CAM", Name = "Camión" });

        var result = await svc.DeleteVehicleTypeAsync(typeId);

        Assert.True(result.Deleted);
        Assert.Empty(await svc.ListVehicleTypesAsync(includeInactive: true));
    }

    [Fact]
    public async Task DeleteZone_SinDependientes_BorraYSusComunas()
    {
        var (svc, db, company) = Build();
        await CommuneSeeder.EnsureSeededAsync(db, company.CompanyId);
        var communes = await svc.ListCommunesAsync();
        var zoneId = await svc.SaveZoneAsync(new Zone
        {
            Code = "NORTE", Name = "Zona norte",
            Communes = { new ZoneCommune { CommuneId = communes[0].Id }, new ZoneCommune { CommuneId = communes[1].Id } },
        });
        Assert.Equal(2, await db.ZoneCommunes.CountAsync());

        var result = await svc.DeleteZoneAsync(zoneId);

        Assert.True(result.Deleted);
        Assert.Empty(await db.Zones.ToListAsync());
        Assert.Empty(await db.ZoneCommunes.ToListAsync());
    }

    [Fact]
    public async Task SaveZone_Editar_SincronizaComunas()
    {
        var (svc, db, company) = Build();
        await CommuneSeeder.EnsureSeededAsync(db, company.CompanyId);
        var communes = await svc.ListCommunesAsync();
        var zoneId = await svc.SaveZoneAsync(new Zone
        {
            Code = "Z", Name = "Z",
            Communes = { new ZoneCommune { CommuneId = communes[0].Id }, new ZoneCommune { CommuneId = communes[1].Id } },
        });

        await svc.SaveZoneAsync(new Zone
        {
            Id = zoneId, Code = "Z", Name = "Z",
            Communes = { new ZoneCommune { CommuneId = communes[1].Id }, new ZoneCommune { CommuneId = communes[2].Id } },
        });

        var ids = (await db.ZoneCommunes.Where(z => z.ZoneId == zoneId).ToListAsync()).Select(z => z.CommuneId).OrderBy(i => i).ToList();
        Assert.Equal(new[] { communes[1].Id, communes[2].Id }.OrderBy(i => i), ids);
    }

    [Fact]
    public async Task ListCarriers_SoloDeLaCompaniaActiva()
    {
        var db = TestDb.Create();
        var (svcA, _, companyA) = Build(db, new FakeCurrentCompanyAccessor());
        var (svcB, _, _) = Build(db, new FakeCurrentCompanyAccessor());
        await svcA.SaveCarrierAsync(new Carrier { Code = "A", Name = "A", Kind = CarrierKinds.Own, TaxId = "76086428-5" });
        await svcB.SaveCarrierAsync(new Carrier { Code = "B", Name = "B", Kind = CarrierKinds.Own, TaxId = "76086428-5" });

        var listA = await svcA.ListCarriersAsync();

        Assert.Equal("A", Assert.Single(listA).Code);
        Assert.Equal(companyA.CompanyId, listA[0].CompanyId);
    }

    [Fact]
    public async Task SaveCarrier_OtraCompania_NoPuedeEditarRegistroAjeno()
    {
        var db = TestDb.Create();
        var (svcA, _, _) = Build(db, new FakeCurrentCompanyAccessor());
        var (svcB, _, _) = Build(db, new FakeCurrentCompanyAccessor());
        var id = await svcA.SaveCarrierAsync(new Carrier { Code = "A", Name = "A", Kind = CarrierKinds.Own, TaxId = "76086428-5" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svcB.SaveCarrierAsync(new Carrier { Id = id, Code = "A", Name = "hack", Kind = CarrierKinds.Own, TaxId = "76086428-5" }));
    }

    [Fact]
    public async Task ListCarriers_OcultaInactivosSalvoPedido()
    {
        var (svc, _, _) = Build();
        await svc.SaveCarrierAsync(new Carrier { Code = "A", Name = "A", Kind = CarrierKinds.Own, TaxId = "76086428-5" });
        await svc.SaveCarrierAsync(new Carrier { Code = "B", Name = "B", Kind = CarrierKinds.Own, TaxId = "76086428-5", IsActive = false });

        Assert.Single(await svc.ListCarriersAsync());
        Assert.Equal(2, (await svc.ListCarriersAsync(includeInactive: true)).Count);
    }

    [Fact]
    public async Task SaveHolidayYRecipient_RechazanDuplicados()
    {
        var (svc, _, _) = Build();
        await svc.SaveHolidayAsync(new Holiday { Date = new DateOnly(2026, 12, 25), Name = "Navidad" });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SaveHolidayAsync(new Holiday { Date = new DateOnly(2026, 12, 25), Name = "Otra" }));

        var user = Guid.NewGuid();
        await svc.SaveNotificationRecipientAsync(new NotificationRecipient { UserId = user });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.SaveNotificationRecipientAsync(new NotificationRecipient { UserId = user }));
    }

    [Fact]
    public async Task ListCommunes_SiembraAutomaticamenteLaPrimeraVez()
    {
        var (svc, _, _) = Build();

        var communes = await svc.ListCommunesAsync();

        Assert.Equal(346, communes.Count);
        Assert.Equal(346, (await svc.ListCommunesAsync()).Count); // idempotente
    }
}
