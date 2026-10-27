using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Lista de manifiestos: último usuario que actualizó cada manifiesto (cabecera, documentos, despacho...).</summary>
public class ManifestListUpdatedByTests
{
    [Fact]
    public async Task Lista_SinCambios_EsQuienLoCreo()
    {
        var env = ManifestTestEnv.Build();
        var creator = env.User.UserId;
        await env.Svc.CreateDraftAsync(env.Header());

        var item = Assert.Single(await env.Svc.ListAsync(new ManifestFilter(null, null, null, null, null)));

        Assert.Equal(creator, item.UpdatedBy);
        Assert.NotNull(item.UpdatedAt);
    }

    [Fact]
    public async Task Lista_DocumentoAgregadoPorOtro_EsElUltimoUsuario()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.Svc.CreateDraftAsync(env.Header());
        var other = Guid.NewGuid();
        env.User.UserId = other;
        env.Clock.Now = env.Clock.Now.AddMinutes(5);
        env.AddSap(10);
        Assert.Equal("added", (await env.Svc.AddDocumentAsync(id, new DocumentCapture(null, DocumentKinds.Invoice, 10))).Outcome);

        var item = Assert.Single(await env.Svc.ListAsync(new ManifestFilter(null, null, null, null, null)));

        Assert.Equal(other, item.UpdatedBy);
        Assert.Equal(env.Clock.Now.UtcDateTime, item.UpdatedAt);
    }

    [Fact]
    public async Task Lista_ImprimirNoCuentaComoActualizacion()
    {
        var env = ManifestTestEnv.Build();
        var creator = env.User.UserId;
        var id = await env.Svc.CreateDraftAsync(env.Header());
        env.User.UserId = Guid.NewGuid();
        env.Clock.Now = env.Clock.Now.AddMinutes(5);
        env.Db.ManifestEvents.Add(new ManifestEvent
        {
            CompanyId = env.Company.CompanyId, ManifestId = id, EventType = ManifestEventTypes.Printed,
            UserId = env.User.UserId, OccurredAt = env.Clock.Now.UtcDateTime,
        });
        await env.Db.SaveChangesAsync();

        var item = Assert.Single(await env.Svc.ListAsync(new ManifestFilter(null, null, null, null, null)));

        Assert.Equal(creator, item.UpdatedBy);
    }
}
