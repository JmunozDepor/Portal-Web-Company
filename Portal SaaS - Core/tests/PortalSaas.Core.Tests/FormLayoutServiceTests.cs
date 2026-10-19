using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Componentes;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Usuarios;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests;

file sealed class UsuarioFijo : ICurrentUserContext
{
    public required Guid UserId { get; init; }
    public string Username => "usuario.prueba";
    public bool IsAdmin { get; init; }
    public required Guid OrganizationId { get; init; }

    public Task<bool> HasActionAsync(string menuCode, string actionCode, CancellationToken ct = default) => Task.FromResult(true);
}

file sealed class CompaniaFija : ICurrentCompanyAccessor
{
    public Guid CompanyId { get; init; }
    public string Code => "C1";
    public string Database => "DB";
    public string ServiceLayerUrl => "https://sl";
    public string Country => "CL";
    public bool HasCompany { get; init; } = true;
}

public class FormLayoutServiceTests
{
    private const string Form = DocumentFormFieldCatalog.Sales;

    private static async Task<(PortalSaasDbContext Db, Guid OrgId, Guid CompanyId)> CrearAsync()
    {
        var db = new PortalSaasDbContext(new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var org = new Organization { LegalName = "Org", Slug = "org", Country = "CL" };
        db.Organizations.Add(org);
        var company = new Company
        {
            OrganizationId = org.Id, Code = "C1", Name = "Compañía 1", DatabaseName = "DB", ServiceLayerUrl = "https://sl",
            IntegrationUsername = "u", IntegrationSecretKey = "k", Country = "CL",
        };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return (db, org.Id, company.Id);
    }

    private static FormLayoutService Servicio(PortalSaasDbContext db, Guid orgId, Guid companyId, Guid userId, bool isAdmin = false, bool hasCompany = true) =>
        new(db, new UsuarioFijo { UserId = userId, OrganizationId = orgId, IsAdmin = isAdmin },
            new CompaniaFija { CompanyId = companyId, HasCompany = hasCompany });

    [Fact]
    public async Task SinNadaGuardado_NoOcultaNada()
    {
        var (db, org, company) = await CrearAsync();
        var layout = await Servicio(db, org, company, Guid.NewGuid()).GetForCurrentUserAsync(Form);

        Assert.Empty(layout.HiddenFields);
        Assert.False(layout.HasUserOverride);
    }

    [Fact]
    public async Task UsuarioSinVersionPropia_VeLaBaseDelAdmin()
    {
        var (db, org, company) = await CrearAsync();
        await Servicio(db, org, company, Guid.NewGuid(), isAdmin: true).SaveCompanyBaseAsync(company, Form, ["linea-marca", "moneda"]);

        var layout = await Servicio(db, org, company, Guid.NewGuid()).GetForCurrentUserAsync(Form);

        Assert.Equal(["linea-marca", "moneda"], layout.HiddenFields);
        Assert.False(layout.HasUserOverride);
    }

    [Fact]
    public async Task VersionPropia_ReemplazaLaBase_YResetVuelveALaBase()
    {
        var (db, org, company) = await CrearAsync();
        var userId = Guid.NewGuid();
        await Servicio(db, org, company, Guid.NewGuid(), isAdmin: true).SaveCompanyBaseAsync(company, Form, ["linea-marca"]);
        var servicio = Servicio(db, org, company, userId);

        await servicio.SaveForCurrentUserAsync(Form, ["comentarios"]);
        var propia = await servicio.GetForCurrentUserAsync(Form);
        await servicio.ResetForCurrentUserAsync(Form);
        var trasReset = await servicio.GetForCurrentUserAsync(Form);

        Assert.Equal(["comentarios"], propia.HiddenFields);
        Assert.Equal(["linea-marca"], propia.BaseHiddenFields);
        Assert.True(propia.HasUserOverride);
        Assert.Equal(["linea-marca"], trasReset.HiddenFields);
        Assert.False(trasReset.HasUserOverride);
    }

    [Fact]
    public async Task VersionDeUnUsuario_NoAfectaAOtro()
    {
        var (db, org, company) = await CrearAsync();
        await Servicio(db, org, company, Guid.NewGuid()).SaveForCurrentUserAsync(Form, ["comentarios"]);

        var otro = await Servicio(db, org, company, Guid.NewGuid()).GetForCurrentUserAsync(Form);

        Assert.Empty(otro.HiddenFields);
    }

    [Fact]
    public async Task ClavesFueraDelCatalogo_NuncaSeGuardan()
    {
        var (db, org, company) = await CrearAsync();
        var servicio = Servicio(db, org, company, Guid.NewGuid());

        // "cliente"/"cantidad" son obligatorios (no están en el catálogo); el selector CSS malicioso tampoco.
        await servicio.SaveForCurrentUserAsync(Form, ["cliente", "cantidad", "x\"]{}body{display:none", "serie", "serie"]);

        Assert.Equal(["serie"], (await servicio.GetForCurrentUserAsync(Form)).HiddenFields);
    }

    [Fact]
    public async Task GuardarDosVeces_ActualizaLaMismaFila()
    {
        var (db, org, company) = await CrearAsync();
        var servicio = Servicio(db, org, company, Guid.NewGuid());

        await servicio.SaveForCurrentUserAsync(Form, ["serie"]);
        await servicio.SaveForCurrentUserAsync(Form, ["moneda"]);

        Assert.Equal(1, await db.FormFieldLayouts.CountAsync());
        Assert.Equal(["moneda"], (await servicio.GetForCurrentUserAsync(Form)).HiddenFields);
    }

    [Fact]
    public async Task BaseDeCompania_SoloAdmin_YSoloDeSuOrganizacion()
    {
        var (db, org, company) = await CrearAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Servicio(db, org, company, Guid.NewGuid()).SaveCompanyBaseAsync(company, Form, ["serie"]));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Servicio(db, Guid.NewGuid(), company, Guid.NewGuid(), isAdmin: true).SaveCompanyBaseAsync(company, Form, ["serie"]));
    }

    [Fact]
    public async Task SinCompaniaActiva_NoOcultaNada()
    {
        var (db, org, company) = await CrearAsync();
        await Servicio(db, org, company, Guid.NewGuid(), isAdmin: true).SaveCompanyBaseAsync(company, Form, ["serie"]);

        var layout = await Servicio(db, org, company, Guid.NewGuid(), hasCompany: false).GetForCurrentUserAsync(Form);

        Assert.Empty(layout.HiddenFields);
    }
}
