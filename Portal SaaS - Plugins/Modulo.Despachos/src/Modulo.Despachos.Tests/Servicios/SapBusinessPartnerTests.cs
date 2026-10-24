using Modulo.Despachos.Data;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using Modulo.Despachos.Tests.Fakes;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Receptor de la guía manual: socio de negocio SAP por RUT (OCRD + direcciones de despacho CRD1).</summary>
public class SapBusinessPartnerTests
{
    private const string Sql = "SELECT 1 AS \"card_code\" FROM DUMMY WHERE :tax_id = :tax_id";

    private static async Task<(SapDocumentLookupService Svc, FakeHanaService Hana)> BuildAsync()
    {
        var db = TestDb.Create();
        var hana = new FakeHanaService { EngineType = "hana" };
        var company = new FakeCurrentCompanyAccessor();
        db.SapDocumentQueries.Add(new SapDocumentQuery
        {
            CompanyId = company.CompanyId, DocumentKind = SapQueryKinds.BusinessPartnerByTaxId, SqlText = Sql, IsActive = true,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (new SapDocumentLookupService(db, company, new FakeCurrentUserContext(), hana), hana);
    }

    private static Dictionary<string, object?> Row(string code = "C76030680-0", string? address = "Av. Las Condes 11774", string active = "1",
        string? addressName = "PARQUE ARAUCO") => new()
    {
        ["card_code"] = code, ["card_name"] = "COMERCIAL DEPOR LIMITADA", ["activity"] = "VENTA AL POR MENOR",
        ["is_active"] = active, ["address_name"] = addressName, ["address"] = address, ["commune"] = "Las Condes", ["city"] = "Santiago",
    };

    private static object? Param(FakeHanaService hana, string name) =>
        hana.LastParameters!.GetType().GetProperty(name)!.GetValue(hana.LastParameters);

    [Fact]
    public async Task RutConPuntos_SeNormaliza()
    {
        var (svc, hana) = await BuildAsync();
        hana.Results[Sql] = new[] { Row() };

        var p = await svc.FindBusinessPartnerAsync("76.030.680-0");

        Assert.Equal("76030680-0", Param(hana, "tax_id"));
        Assert.Equal(("C76030680-0", "COMERCIAL DEPOR LIMITADA", "VENTA AL POR MENOR", true), (p!.CardCode, p.Name, p.Activity, p.IsActive));
    }

    [Fact]
    public async Task DosDirecciones_UnSocio()
    {
        var (svc, hana) = await BuildAsync();
        hana.Results[Sql] = new[] { Row(), Row(address: "Estado 387", addressName: "ESTADO") };

        var p = await svc.FindBusinessPartnerAsync("76030680-0");

        Assert.Equal(new[] { "Av. Las Condes 11774", "Estado 387" }, p!.Addresses.Select(a => a.Address));
        Assert.Equal("Las Condes", p.Addresses[0].Commune);
    }

    [Fact]
    public async Task SinDireccionDeDespacho_SocioSinDirecciones()
    {
        var (svc, hana) = await BuildAsync();
        hana.Results[Sql] = new[] { Row(address: null, addressName: null) };
        Assert.Empty((await svc.FindBusinessPartnerAsync("76030680-0"))!.Addresses);
    }

    [Fact]
    public async Task VariosSocios_PrefiereElActivo()
    {
        var (svc, hana) = await BuildAsync();
        hana.Results[Sql] = new[] { Row(code: "C1", active: "0"), Row(code: "C2") };
        Assert.Equal("C2", (await svc.FindBusinessPartnerAsync("76030680-0"))!.CardCode);
    }

    [Fact]
    public async Task SinFilas_Null()
    {
        var (svc, _) = await BuildAsync();
        Assert.Null(await svc.FindBusinessPartnerAsync("76030680-0"));
    }

    [Fact]
    public async Task RutInvalido_Lanza()
    {
        var (svc, _) = await BuildAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.FindBusinessPartnerAsync("12.345.678-0"));
        Assert.Contains("no es válido", ex.Message);
    }

    [Fact]
    public async Task Probar_ConRutDeMuestra_DevuelveSocio()
    {
        var (svc, hana) = await BuildAsync();
        hana.Results[Sql] = new[] { Row() };

        var r = await svc.ProbeAsync(SapQueryKinds.BusinessPartnerByTaxId, Sql, 0, "76.030.680-0");

        Assert.True(r.Validation.IsValid, r.Validation.Error);
        Assert.Equal("C76030680-0", r.Partner!.CardCode);
    }

    [Fact]
    public async Task Probar_OtroParametro_Invalida()
    {
        var (svc, _) = await BuildAsync();
        var r = await svc.ProbeAsync(SapQueryKinds.BusinessPartnerByTaxId, "SELECT 1 FROM DUMMY WHERE :folio = 1", 0, "76030680-0");
        Assert.False(r.Validation.IsValid);
    }

    [Fact]
    public void Seed_UsaSoloTaxId_YSinParametrosEnComentarios()
    {
        var seed = SapQuerySeeds.Get(SapQueryKinds.BusinessPartnerByTaxId);
        Assert.Contains(":tax_id", seed);
        Assert.Contains("CRD1", seed);
        Assert.DoesNotContain(seed.Split('\n'), l => l.TrimStart().StartsWith("--") && l.Contains(':'));
    }
}
