using ClosedXML.Excel;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

public class DocumentReportServiceTests
{
    /// <summary>
    /// Manifiesto A (folios 1 y 2) con retorno: 1 entregado, 2 rechazado con motivo y comentario.
    /// Manifiesto B (folio 3) despachado sin retorno. Borrador C (folio 4) y anulado D (folio 5) no cuentan.
    /// </summary>
    private static async Task<(ManifestTestEnv Env, DocumentReportService Rep, int NumberA, int NumberB)> BuildAsync()
    {
        var env = ManifestTestEnv.Build();
        env.AddSap(1, total: 100000);
        env.AddSap(2, total: 50000);
        env.AddSap(3, commune: "Curicó", total: 30000);

        var a = await env.DraftWithDocsAsync(1, 2);
        Assert.True((await env.Svc.DispatchAsync(a)).Success);
        var docsA = (await env.Svc.GetAsync(a))!.Documents.OrderBy(d => d.Folio).ToList();
        var reason = await env.Master.SaveRejectionReasonAsync(new RejectionReason
        { Code = "DAN", Name = "Producto dañado", AppliesTo = RejectionAppliesTo.Rejection });
        await env.Svc.RegisterReturnAsync(a, new[]
        {
            new DocumentReturnInput(docsA[0].Id, DeliveryStatuses.Delivered, null, null),
            new DocumentReturnInput(docsA[1].Id, DeliveryStatuses.Rejected, reason, "Caja rota"),
        }, null);

        var b = await env.DraftWithDocsAsync(3);
        Assert.True((await env.Svc.DispatchAsync(b)).Success);

        await env.DraftWithDocsAsync(4);
        var d = await env.DraftWithDocsAsync(5);
        await env.Svc.AnnulAsync(d, "Prueba");

        var numberA = (await env.Svc.GetAsync(a))!.Number;
        var numberB = (await env.Svc.GetAsync(b))!.Number;
        return (env, new DocumentReportService(env.Db, env.Company), numberA, numberB);
    }

    private static DocumentReportFilter Day(ManifestTestEnv env, string? status = null, long? carrier = null, string? search = null) =>
        new(ManifestTestEnv.Fecha, ManifestTestEnv.Fecha, status, carrier, search);

    [Fact]
    public async Task Get_AgrupaPorManifiesto_SoloDespachados_ConEstadoMotivoYComentario()
    {
        var (env, rep, numberA, numberB) = await BuildAsync();
        var r = await rep.GetAsync(Day(env));

        Assert.Equal(new[] { numberA, numberB }, r.Manifests.Select(m => m.Number));
        Assert.Equal(3, r.TotalDocuments);
        Assert.Equal(180000m, r.TotalAmount);
        var a = r.Manifests[0];
        Assert.Equal(ManifestStatuses.Returned, a.Status);
        Assert.Equal("TSM", a.Carrier);
        Assert.Equal(150000m, a.Amount);
        var rejected = a.Documents.Single(x => x.Folio == 2);
        Assert.Equal(DeliveryStatuses.Rejected, rejected.DeliveryStatus);
        Assert.Equal("Producto dañado", rejected.RejectionReason);
        Assert.Equal("Caja rota", rejected.Comments);
        Assert.Equal(DeliveryStatuses.Dispatched, r.Manifests[1].Documents.Single().DeliveryStatus);
        Assert.Equal(1, r.CountByStatus[DeliveryStatuses.Delivered]);
        Assert.Equal(1, r.CountByStatus[DeliveryStatuses.Rejected]);
        Assert.Equal(1, r.CountByStatus[DeliveryStatuses.Dispatched]);
    }

    [Fact]
    public async Task Get_FiltraPorFechaEstadoTransportistaYBusqueda()
    {
        var (env, rep, numberA, numberB) = await BuildAsync();

        Assert.Empty((await rep.GetAsync(new DocumentReportFilter(ManifestTestEnv.Fecha.AddDays(1), ManifestTestEnv.Fecha.AddDays(5)))).Manifests);

        var rejected = await rep.GetAsync(Day(env, status: DeliveryStatuses.Rejected));
        Assert.Equal(numberA, Assert.Single(rejected.Manifests).Number);
        Assert.Equal(2, Assert.Single(rejected.Manifests[0].Documents).Folio);

        Assert.Empty((await rep.GetAsync(Day(env, carrier: 999999))).Manifests);
        Assert.Equal(2, (await rep.GetAsync(Day(env, carrier: env.CarrierId))).Manifests.Count);

        Assert.Equal(3, Assert.Single((await rep.GetAsync(Day(env, search: "3"))).Manifests).Documents.Single().Folio);
        // Un número puede calzar con un folio y con un N° de manifiesto a la vez: trae ambos.
        var byNumber = await rep.GetAsync(Day(env, search: numberB.ToString()));
        Assert.Contains(byNumber.Manifests, m => m.Number == numberB && m.Documents.Count == 1);
        Assert.Equal(3, (await rep.GetAsync(Day(env, search: "cliente"))).TotalDocuments);
    }

    [Fact]
    public async Task DocumentReport_ExcluyeGuiasPendientes()
    {
        var (env, rep, _, numberB) = await BuildAsync();
        var b = env.Db.Manifests.Single(m => m.Number == numberB);
        b.Status = ManifestStatuses.PendingGuides;
        await env.Db.SaveChangesAsync();

        var r = await rep.GetAsync(Day(env));
        Assert.DoesNotContain(r.Manifests, m => m.Number == numberB);
    }

    [Fact]
    public async Task Get_RangoInvalido_Falla()
    {
        var (env, rep, _, _) = await BuildAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rep.GetAsync(new DocumentReportFilter(ManifestTestEnv.Fecha, ManifestTestEnv.Fecha.AddDays(-1))));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rep.GetAsync(new DocumentReportFilter(ManifestTestEnv.Fecha, ManifestTestEnv.Fecha.AddDays(IDocumentReportService.MaxRangeDays + 1))));
    }

    [Fact]
    public async Task ExportExcel_UnaFilaPorDocumento_YResumen()
    {
        var (env, rep, numberA, _) = await BuildAsync();
        var bytes = await rep.ExportExcelAsync(Day(env));

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Documentos");
        Assert.Equal(4, ws.RowsUsed().Count()); // encabezado + 3 documentos
        var row = ws.RowsUsed().Skip(1).Single(x => x.Cell(10).GetValue<long>() == 2);
        Assert.Equal(numberA, row.Cell(1).GetValue<int>());
        Assert.Equal("Rechazado", row.Cell(16).GetString());
        Assert.Equal("Producto dañado", row.Cell(17).GetString());
        Assert.Equal("Caja rota", row.Cell(18).GetString());
        Assert.Contains(wb.Worksheet("Resumen").RowsUsed(), x => x.Cell(1).GetString() == "Entregado" && x.Cell(2).GetValue<int>() == 1);
    }
}
