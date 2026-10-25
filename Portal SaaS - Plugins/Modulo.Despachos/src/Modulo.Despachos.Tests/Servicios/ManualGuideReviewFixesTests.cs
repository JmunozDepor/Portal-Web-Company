using ClosedXML.Excel;
using Modulo.Despachos.Dominio.Guias;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

/// <summary>Correcciones de la revisión final de guías manuales.</summary>
public class ManualGuideReviewFixesTests
{
    [Fact]
    public async Task Emitir_PendienteReintentadaOtroDia_MismoContenido_YNoQuedaRechazada()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        env.Dte.Down = true;
        Assert.Equal(TransportGuideStatuses.Pending, (await env.ManualGuides.EmitAsync(id)).Status);

        env.Clock.Now = env.Clock.Now.AddDays(1);
        env.Dte.Down = false;
        var g = await env.ManualGuides.EmitAsync(id);

        Assert.Equal(TransportGuideStatuses.Issued, g.Status);
        Assert.Equal(ManifestTestEnv.Fecha, g.IssueDate);
        Assert.All(env.Dte.Calls, c => Assert.Equal(ManifestTestEnv.Fecha, c.Document.IdDoc.IssueDate));
    }

    [Fact]
    public async Task Emitir_PasarelaRechazaPorContenido_ConEmisionVigente_NoMarcaRechazada()
    {
        var env = ManifestTestEnv.Build();
        var id = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        env.Dte.Down = true;
        await env.ManualGuides.EmitAsync(id);
        // Simula una emisión vigente con otro contenido en la pasarela (p. ej. de otra versión de la guía).
        var key = (await env.ManualGuides.GetAsync(id))!.EmissionKey;
        env.Dte.Contents[key] = "otro";
        env.Dte.Down = false;

        var g = await env.ManualGuides.EmitAsync(id);

        Assert.Equal(TransportGuideStatuses.Pending, g.Status);
        Assert.Empty(env.Notifications.Enqueued);
    }

    [Fact]
    public void Xlsx_CantidadConTresDecimales_NoSeMultiplica()
    {
        using var wb = new XLWorkbook(new MemoryStream(ManualGuideLineImporter.TemplateXlsx()));
        var ws = wb.Worksheet(1);
        ws.Cell(2, 2).Value = "Cinta"; ws.Cell(2, 3).Value = 0.125; ws.Cell(2, 5).Value = 2.25;
        ws.Cell(3, 2).Value = "Caja"; ws.Cell(3, 3).Value = 1500; ws.Cell(3, 5).Value = 608;
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var r = ManualGuideLineImporter.FromXlsx(ms);

        Assert.Empty(r.Errors);
        Assert.Equal(new[] { 0.125m, 1500m }, r.Lines.Select(l => l.Quantity));
        Assert.Equal(2.25m, r.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task Editar_GuiaEnManifiesto_ActualizaElDocumentoDelManifiesto()
    {
        var env = ManifestTestEnv.Build();
        var gid = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        var mid = await env.Svc.CreateDraftAsync(env.Header());
        await env.Svc.AddManualGuideByIdAsync(mid, gid);

        await env.ManualGuides.UpdateAsync(gid, ManualGuideServiceTests.Input(env, price: 100000) with
        {
            ReceiverName = "Patricio Vera", DestinationAddress = "Pasaje Santiago 470", DestinationCommune = "Curicó",
        });

        var d = (await env.Svc.GetAsync(mid))!.Documents.Single();
        Assert.Equal(("Patricio Vera", "Pasaje Santiago 470", "Curicó", env.CuricoId, 119000m),
            (d.CustomerName, d.DestinationAddress, d.DestinationCommuneName, d.DestinationCommuneId, d.TotalAmount));
    }

    [Fact]
    public async Task Lista_BorradoresSinFolioArriba_LuegoPorFolioDescendente()
    {
        var env = ManifestTestEnv.Build();
        var issued1 = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        var issued2 = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        await env.ManualGuides.EmitAsync(issued1);
        await env.ManualGuides.EmitAsync(issued2);
        var draft = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env) with { IssueDate = ManifestTestEnv.Fecha.AddDays(-5) });

        var r = await env.ManualGuides.ListAsync(new GuideFilter(null, null, null, null, null));

        Assert.Equal(new[] { draft, issued2, issued1 }, r.Select(x => x.Id));
    }

    [Fact]
    public async Task Lista_FiltroPorFolio()
    {
        var env = ManifestTestEnv.Build();
        var a = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        var b = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env));
        await env.ManualGuides.EmitAsync(a);
        await env.ManualGuides.EmitAsync(b);

        var r = await env.ManualGuides.ListAsync(new GuideFilter(null, null, null, null, null, Folio: 501));

        Assert.Equal(b, Assert.Single(r).Id);
    }

    [Fact]
    public async Task Lista_FiltroDeFechas_NoSeCortaEnLasMasRecientes()
    {
        var env = ManifestTestEnv.Build();
        var old = await env.ManualGuides.CreateAsync(ManualGuideServiceTests.Input(env) with { IssueDate = new DateOnly(2026, 11, 10) });
        var oldRow = env.Db.TransportGuides.Single(g => g.Id == old);
        oldRow.CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        oldRow.IssueDate = new DateOnly(2026, 1, 1);
        for (var i = 0; i < 1001; i++)
            env.Db.TransportGuides.Add(new TransportGuide
            {
                Id = Guid.NewGuid(), CompanyId = env.Company.CompanyId, Source = TransportGuideSources.Manifest, EmissionKey = Guid.NewGuid(),
                Status = TransportGuideStatuses.Issued, ReceiverTaxId = "11111111-1", ReceiverName = "Cliente", DestinationAddress = "x",
                DestinationCommune = "Rancagua", DocumentJson = "{}", CreatedAt = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc),
            });
        await env.Db.SaveChangesAsync();

        var r = await env.ManualGuides.ListAsync(new GuideFilter(null, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), null));

        Assert.Equal(old, Assert.Single(r).Id);
    }
}
