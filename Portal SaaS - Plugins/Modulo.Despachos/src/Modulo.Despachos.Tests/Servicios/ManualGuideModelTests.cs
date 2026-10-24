using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Models;
using Modulo.Despachos.Tests.Fakes;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>transport_guides como índice de todas las guías: una guía manual existe sin manifiesto.</summary>
public class ManualGuideModelTests
{
    [Fact]
    public async Task Guia_SinManifiesto_SeGuarda()
    {
        var company = Guid.NewGuid();
        var name = "manual-model-" + company;
        await using (var db = TestDb.Create(name))
        {
            db.TransportGuides.Add(new TransportGuide
            {
                Id = Guid.NewGuid(), CompanyId = company, Source = TransportGuideSources.Manual, ManifestId = null, Sequence = null,
                Number = 1, EmissionKey = Guid.NewGuid(), Status = TransportGuideStatuses.Draft,
                ReceiverTaxId = "76030680-0", ReceiverName = "COMERCIAL DEPOR LIMITADA",
                DestinationAddress = "Av. Las Condes 11774", DestinationCommune = "Vitacura", DocumentJson = "{}",
            });
            await db.SaveChangesAsync();
        }
        await using var read = TestDb.Create(name);
        var g = await read.TransportGuides.SingleAsync(x => x.CompanyId == company);
        Assert.Equal(TransportGuideSources.Manual, g.Source);
        Assert.Null(g.ManifestId);
        Assert.Equal(1, g.Number);
        Assert.Equal("Borrador", TransportGuideStatuses.Label(g.Status));
    }

    [Fact]
    public void GuiaManual_NoEsBuscablePorFolio()
    {
        Assert.DoesNotContain(DocumentKinds.ManualGuide, DocumentKinds.All);
        Assert.Equal("Guía manual", DocumentKinds.Label(DocumentKinds.ManualGuide));
    }
}
