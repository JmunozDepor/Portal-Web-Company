using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Dominio.Reglas;
using Modulo.FacturasProveedor.Models;
using Modulo.FacturasProveedor.Servicios;
using Modulo.FacturasProveedor.Tests.Servicios;
using PortalSaas.Abstractions.Modelos;
using PortalSaas.Abstractions.Modelos.Dte;
using Bandeja = Modulo.FacturasProveedor.Pages.FacturasProveedor.Bandeja;
using Mias = Modulo.FacturasProveedor.Pages.FacturasProveedor.MisAprobaciones;

namespace Modulo.FacturasProveedor.Tests.Pages;

public class PaginasOperativasTests
{
    private const string Menu = "FacturasProveedor.bandeja";

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private static TempDataDictionary Temp() => new(new DefaultHttpContext(), new NullTempDataProvider());

    private static ReviewDecisionService Decisions(ReviewTestEnv env) =>
        new(env.Db, env.Inbox, env.Sap, env.Profiles, env.Groups, env.Reviews, env.Engine, env.Settings, env.User, env.Company, env.Clock);

    private static ReviewQueryService Queries(ReviewTestEnv env) =>
        new(env.Db, env.Inbox, env.Groups, env.Profiles, Decisions(env), env.Settings, env.User, env.Company, env.Clock);

    private static Bandeja.DetalleModel Detalle(ReviewTestEnv env) =>
        new(Queries(env), Decisions(env), env.Inbox, env.User)
        {
            TempData = Temp(), PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task Bandeja_SinVer_Forbid()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var page = new Bandeja.IndexModel(Queries(env), env.Inbox, env.Reviews, env.Settings, env.User) { TempData = Temp() };

        Assert.IsType<ForbidResult>(await page.OnGetAsync());
        env.User.Grant(Menu, PortalActions.View);
        Assert.IsType<PageResult>(await page.OnGetAsync());
        Assert.True(page.IsSimulation);
    }

    [Fact]
    public async Task Detalle_SinEditar_NoMuestraAsociar()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var doc = env.Invoice(purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        var review = await env.Db.InvoiceReviews.SingleAsync();
        env.User.Grant(Menu, PortalActions.View);

        var page = Detalle(env);
        Assert.IsType<PageResult>(await page.OnGetAsync(review.Id));
        Assert.False(page.Detail!.CanAssociate);
        Assert.Equal(doc.Folio, page.Detail.Row.Folio);

        env.User.Grant(Menu, PortalActions.Edit);
        await page.OnGetAsync(review.Id);
        Assert.True(page.Detail!.CanAssociate);
    }

    [Fact]
    public async Task Detalle_PostAceptar_SinPermiso_Forbid()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Invoice(purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        var review = await env.Db.InvoiceReviews.SingleAsync();

        var page = Detalle(env);
        Assert.IsType<ForbidResult>(await page.OnPostAceptarAsync(review.Id, review.ConcurrencyStamp));
        Assert.IsType<ForbidResult>(await page.OnPostAsociarOcAsync(review.Id, 4512));
        Assert.IsType<ForbidResult>(await page.OnGetAsync(review.Id));
    }

    [Fact]
    public async Task Detalle_OtraCompania_NotFound()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Invoice(purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        var review = await env.Db.InvoiceReviews.SingleAsync();
        env.User.Grant(Menu, PortalActions.View);
        env.Company.CompanyId = Guid.NewGuid();

        Assert.IsType<NotFoundResult>(await Detalle(env).OnGetAsync(review.Id));
    }

    [Fact]
    public async Task MisAprobaciones_SoloDeMisGrupos()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var me = Guid.NewGuid();
        var mine = await env.Groups.SaveAsync(new ApproverGroupInput(null, "Mío", false, true, new[] { me }), CancellationToken.None);
        var other = await env.Groups.SaveAsync(new ApproverGroupInput(null, "Otro", false, true, new[] { Guid.NewGuid() }), CancellationToken.None);
        await env.Profiles.SaveAsync(new SupplierProfileInput(null, "77555010-K", SupplierConditions.FixedMonthly, mine, null,
            new DateOnly(2026, 10, 1), null, null, true, Array.Empty<RuleOverrideInput>()), CancellationToken.None);
        await env.Profiles.SaveAsync(new SupplierProfileInput(null, "76412553-1", SupplierConditions.FixedMonthly, other, null,
            new DateOnly(2026, 10, 1), null, null, true, Array.Empty<RuleOverrideInput>()), CancellationToken.None);
        env.Invoice(folio: 1, taxId: "77555010-K", purchaseOrders: Array.Empty<int>());
        env.Invoice(folio: 2, taxId: "76412553-1", purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        env.User.UserId = me;
        env.User.Grant("FacturasProveedor.mis-aprobaciones", PortalActions.View);

        var page = new Mias.IndexModel(Queries(env), env.User) { TempData = Temp() };
        Assert.IsType<PageResult>(await page.OnGetAsync());

        Assert.Equal(1, Assert.Single(page.Rows).Folio);
    }

    /// <summary>
    /// I2 (revisión final): con solo "Mis aprobaciones" el aprobador abre y decide las facturas de sus grupos (spec §13),
    /// pero no las de otros grupos ni puede asociar OC.
    /// </summary>
    [Fact]
    public async Task Detalle_AprobadorSoloConMisAprobaciones_DecideLoDeSuGrupo()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var me = Guid.NewGuid();
        var mine = await env.Groups.SaveAsync(new ApproverGroupInput(null, "Mío", false, true, new[] { me }), CancellationToken.None);
        var other = await env.Groups.SaveAsync(new ApproverGroupInput(null, "Otro", false, true, new[] { Guid.NewGuid() }), CancellationToken.None);
        await env.Profiles.SaveAsync(new SupplierProfileInput(null, "77555010-K", SupplierConditions.FixedMonthly, mine, null,
            new DateOnly(2026, 10, 1), null, null, true, Array.Empty<RuleOverrideInput>()), CancellationToken.None);
        await env.Profiles.SaveAsync(new SupplierProfileInput(null, "76412553-1", SupplierConditions.FixedMonthly, other, null,
            new DateOnly(2026, 10, 1), null, null, true, Array.Empty<RuleOverrideInput>()), CancellationToken.None);
        env.Invoice(folio: 1, taxId: "77555010-K", purchaseOrders: Array.Empty<int>());
        env.Invoice(folio: 2, taxId: "76412553-1", purchaseOrders: Array.Empty<int>());
        await env.EvaluateAsync();
        var myReview = await env.Db.InvoiceReviews.SingleAsync(r => r.Folio == 1);
        var otherReview = await env.Db.InvoiceReviews.SingleAsync(r => r.Folio == 2);
        env.User.UserId = me;
        env.User.Grant("FacturasProveedor.mis-aprobaciones", PortalActions.View);

        var page = Detalle(env);
        Assert.IsType<PageResult>(await page.OnGetAsync(myReview.Id));
        Assert.True(page.Detail!.CanDecide);
        Assert.IsType<ForbidResult>(await page.OnGetAsync(otherReview.Id));
        Assert.IsType<ForbidResult>(await page.OnPostAsociarOcAsync(myReview.Id, 4512));

        Assert.IsType<RedirectToPageResult>(await page.OnPostAceptarAsync(myReview.Id, myReview.ConcurrencyStamp));
        Assert.Equal(RuleActions.Accept, (await env.Db.InvoiceReviews.AsNoTracking().SingleAsync(r => r.Id == myReview.Id)).DecidedAction);
        Assert.IsType<PageResult>(await page.OnGetAsync(myReview.Id)); // sigue viendo la que ya decidió
    }

    private sealed class CollidingDecisions : IReviewDecisionService
    {
        public Task<bool> CanDecideAsync(long reviewId, CancellationToken ct) => Task.FromResult(true);
        public Task DecideAsync(long reviewId, string action, string? reason, string? claimType, Guid concurrencyStamp, CancellationToken ct) =>
            throw new DbUpdateException("duplicate key value violates unique constraint");
        public Task<string?> GetAcceptWarningAsync(long reviewId, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task AssociatePurchaseOrderAsync(long reviewId, int docNum, CancellationToken ct) =>
            throw new DbUpdateException("duplicate key value violates unique constraint");
    }

    /// <summary>I7 (revisión final): un choque de base con el job se muestra como mensaje, no como error 500.</summary>
    [Fact]
    public async Task Detalle_ChoqueDeBaseConElJob_MuestraMensaje()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.User.Grant(Menu, PortalActions.View, PortalActions.Edit);
        var page = new Bandeja.DetalleModel(Queries(env), new CollidingDecisions(), env.Inbox, env.User)
        {
            TempData = Temp(), PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
        };

        Assert.IsType<RedirectToPageResult>(await page.OnPostAsociarOcAsync(1, 4512));
        Assert.Contains("otro proceso", page.ErrorMessage);
    }

    [Fact]
    public async Task Kpis_CuentaVencenEn2Dias()
    {
        var env = await new ReviewTestEnv().StartAsync(processFrom: new DateOnly(2026, 10, 1));
        env.Invoice(folio: 1, day: 20, purchaseOrders: Array.Empty<int>()); // vence 28-10
        env.Invoice(folio: 2, day: 24, purchaseOrders: Array.Empty<int>()); // vence 01-11
        await env.EvaluateAsync();

        var kpis = await Queries(env).GetKpisAsync(CancellationToken.None);

        Assert.Equal(2, kpis.Warning);
        Assert.Equal(1, kpis.DueIn2Days);
    }

    [Fact]
    public async Task List_Pendientes_IncluyeAvisoRevisionYPorEnviar()
    {
        var env = await new ReviewTestEnv().StartAsync(ModuleModes.Active);
        env.Sap.AddPurchaseOrder();
        env.Invoice(folio: 1); // por enviar
        env.Invoice(folio: 2, purchaseOrders: Array.Empty<int>()); // aviso
        env.Invoice(folio: 3, purchaseOrders: new[] { 4512 }, total: 9_000_000, taxId: "76412553-1", day: 25); // rechazo → por enviar
        await env.EvaluateAsync();
        var accepted = await env.Db.InvoiceReviews.FirstAsync(r => r.Folio == 1);
        accepted.Status = ReviewStatuses.Accepted;
        await env.Db.SaveChangesAsync();

        var rows = await Queries(env).ListAsync(new ReviewListFilter(null, null, "pending", null), CancellationToken.None);

        Assert.Equal(new long[] { 2, 3 }, rows.Select(r => r.Folio).OrderBy(f => f));
        Assert.Contains(rows, r => r.Folio == 2 && r.Summary!.Contains("no referencia"));
    }

    /// <summary>C3 (revisión final): una factura que no se pudo evaluar (queda "nueva") se ve en Pendientes.</summary>
    [Fact]
    public async Task List_Pendientes_IncluyeNuevasQueNoSePudieronEvaluar()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.FailingTaxIds.Add("76412553-1");
        env.Invoice(folio: 5);
        await env.EvaluateAsync();

        var rows = await Queries(env).ListAsync(new ReviewListFilter(null, null, "pending", null), CancellationToken.None);

        Assert.Equal(new long[] { 5 }, rows.Select(r => r.Folio));
    }

    private static async Task<ReviewTestEnv> ThirtyInvoicesAsync()
    {
        var env = await new ReviewTestEnv().StartAsync();
        for (var folio = 1; folio <= 30; folio++)
            env.Inbox.Add(folio: folio, siiReceivedAt: ReviewTestEnv.ReceivedAt(21).AddMinutes(folio), purchaseOrders: new[] { 4512 });
        await env.EvaluateAsync();
        return env;
    }

    [Fact]
    public async Task ListPage_PaginaYTotal_MasRecientesPrimero()
    {
        var env = await ThirtyInvoicesAsync();

        var page = await Queries(env).ListPageAsync(new ReviewListFilter(null, null, "all", null), 2, 25, CancellationToken.None);

        Assert.Equal(30, page.Total);
        Assert.Equal(new long[] { 5, 4, 3, 2, 1 }, page.Rows.Select(r => r.Folio));
    }

    [Fact]
    public async Task ListPage_TextoPorOc_YRazonSocialSinMayusculas()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Inbox.Add(folio: 1, purchaseOrders: new[] { 4512 });
        env.Inbox.Add(folio: 2, purchaseOrders: new[] { 4530 }, issuerTaxId: "77555010-K", issuerName: "Inmobiliaria Los Robles SpA");
        await env.EvaluateAsync();

        var byPo = await Queries(env).ListPageAsync(new ReviewListFilter(null, null, "all", "4530"), 1, 25, CancellationToken.None);
        var byName = await Queries(env).ListPageAsync(new ReviewListFilter(null, null, "all", "los robles"), 1, 25, CancellationToken.None);

        Assert.Equal(1, byPo.Total);
        Assert.Equal(2, byPo.Rows.Single().Folio);
        Assert.Equal(2, byName.Rows.Single().Folio);
    }

    /// <summary>Columnas nuevas de la bandeja: hora de registro SII, forma de pago, vencimiento, PDF y documento referenciado.</summary>
    [Fact]
    public async Task List_Fila_TraeRegistroFormaDePagoPdfYReferenciaDeLaNota()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var note = env.Inbox.Add(dteType: 61, folio: 515, siiReceivedAt: ReviewTestEnv.ReceivedAt(24), purchaseOrders: Array.Empty<int>(),
            paymentForm: "CREDITO", dueDate: new DateOnly(2026, 11, 12), hasPdf: true,
            references: new[] { new DteReceivedReference("33", "4045", new DateOnly(2026, 9, 4), 1, "ANULA DOCUMENTO") });
        await env.EvaluateAsync();

        var row = (await Queries(env).ListPageAsync(new ReviewListFilter(null, null, "all", null), 1, 25, CancellationToken.None)).Rows.Single();

        Assert.Equal(note.SiiReceivedAt, row.SiiReceivedAt);
        Assert.Equal("CREDITO", row.PaymentForm);
        Assert.Equal(new DateOnly(2026, 11, 12), row.DueDate);
        Assert.True(row.HasPdf);
        Assert.Equal("Factura 4045", row.ReferenceLabel);
    }

    private static Bandeja.IndexModel BandejaPage(ReviewTestEnv env) =>
        new(Queries(env), env.Inbox, env.Reviews, env.Settings, env.User)
        {
            TempData = Temp(),
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext(),
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            },
        };

    [Fact]
    public async Task Bandeja_Paginada_ExponeTotalYPaginas_YCorrigeTamanoInvalido()
    {
        var env = await ThirtyInvoicesAsync();
        env.User.Grant(Menu, PortalActions.View);
        var page = BandejaPage(env);
        page.Estado = "all";
        page.PageNumber = 2;
        page.PageSize = 7;

        await page.OnGetAsync();

        Assert.Equal(25, page.PageSize);
        Assert.Equal(30, page.TotalRecords);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(5, page.Rows.Count);
        Assert.Equal(8, page.LegalDeadlineDays);
    }

    [Fact]
    public async Task Bandeja_VistaPrevia_DevuelveElDocumento_YRespetaPermisos()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Invoice();
        await env.EvaluateAsync();
        var review = await env.Db.InvoiceReviews.SingleAsync();
        var page = BandejaPage(env);

        Assert.IsType<ForbidResult>(await page.OnGetVistaPreviaAsync(review.Id));
        env.User.Grant(Menu, PortalActions.View);
        var result = Assert.IsType<PartialViewResult>(await page.OnGetVistaPreviaAsync(review.Id));
        Assert.Equal("_VistaPrevia", result.ViewName);
        Assert.Equal(18452, Assert.IsType<ReviewDetail>(result.Model).Row.Folio);
        Assert.IsType<NotFoundResult>(await page.OnGetVistaPreviaAsync(review.Id + 99));
    }

    [Fact]
    public async Task Bandeja_Excel_ExportaTodoLoFiltrado_NoSoloLaPagina()
    {
        var env = await ThirtyInvoicesAsync();
        env.User.Grant(Menu, PortalActions.View);
        var page = BandejaPage(env);
        page.Estado = "all";

        var file = Assert.IsType<FileContentResult>(await page.OnGetExcelAsync());

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.EndsWith(".xlsx", file.FileDownloadName);
        using var book = new XLWorkbook(new MemoryStream(file.FileContents));
        var sheet = book.Worksheets.First();
        Assert.Equal("Fecha registro SII", sheet.Cell(1, 1).GetString());
        Assert.Equal("Razón social", sheet.Cell(1, 3).GetString());
        Assert.Equal(31, sheet.LastRowUsed()!.RowNumber());
    }

    [Fact]
    public async Task Detalle_PdfQueElOperadorNoEntrega_MensajeEnVezDe404()
    {
        var env = await new ReviewTestEnv().StartAsync();
        var doc = env.Invoice();
        await env.EvaluateAsync();
        var review = await env.Db.InvoiceReviews.SingleAsync();
        env.User.Grant(Menu, PortalActions.View);

        var missing = Assert.IsType<RedirectToPageResult>(await Detalle(env).OnGetPdfAsync(review.Id));
        Assert.Equal(review.Id, missing.RouteValues!["id"]);

        env.Inbox.Pdfs[doc.Id] = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var file = Assert.IsType<FileContentResult>(await Detalle(env).OnGetPdfAsync(review.Id));
        Assert.Equal("application/pdf", file.ContentType);
    }

    [Fact]
    public async Task List_Simulacion_MarcaCoincidencia()
    {
        var env = await new ReviewTestEnv().StartAsync();
        env.Sap.AddPurchaseOrder().AddPurchaseOrder(docNum: 4530);
        var ok = env.Invoice(folio: 1, total: 1_000_000);
        var bad = env.Invoice(folio: 2, total: 9_000_000, purchaseOrders: new[] { 4530 });
        var open = env.Invoice(folio: 3, total: 1_000_000, purchaseOrders: new[] { 4530 }, day: 25);
        await env.EvaluateAsync();
        env.Inbox.Replace(ok with { ResponseStatus = DteResponseStatuses.External, ResponseAction = DteResponseActions.AcceptContent });
        env.Inbox.Replace(bad with { ResponseStatus = DteResponseStatuses.External, ResponseAction = DteResponseActions.AcceptContent });

        var rows = await Queries(env).ListAsync(new ReviewListFilter(null, null, "simulation", null), CancellationToken.None);

        Assert.True(rows.Single(r => r.Folio == 1).Matches);
        Assert.False(rows.Single(r => r.Folio == 2).Matches);
        Assert.Null(rows.Single(r => r.Folio == open.Folio).Matches);
    }
}
