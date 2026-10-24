using Modulo.Despachos.Dominio.Guias;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.Despachos.Tests.Dominio.Guias;

public class GuidePlannerTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 10);
    private const long Talca = 1, Curico = 2;

    private static Manifest Manifiesto(DateOnly? llegada = null) => new()
    {
        Id = Guid.NewGuid(), Number = 15, Plate = "ABCD12", TrailerPlate = null, CarrierTaxId = "76030680-0",
        DriverTaxId = "18504907-8", DriverName = "Eduardo Escobar", OriginAddress = "Camino San Esteban 1361",
        OriginCommune = "San Bernardo", EstimatedArrivalDate = llegada ?? Hoy,
    };

    private static ManifestDocument Factura(long folio, string rut = "76086428-5", string dir = "Av. Uno 100", long? comuna = Talca,
        int bultos = 2, string kind = DocumentKinds.Invoice) => new()
    {
        Id = Guid.NewGuid(), DocumentKind = kind, SiiDocumentType = 33, Folio = folio, SapDocEntry = (int)folio,
        DocumentDate = Hoy.AddDays(-1), CustomerName = $"Cliente {rut}", CustomerTaxId = rut, DestinationAddress = dir,
        DestinationCommuneId = comuna, Packages = bultos,
    };

    private static SapDocumentLines Lineas(params (string Item, decimal Qty, decimal? Kg)[] items) =>
        new("Venta al por menor", "Talca",
            items.Select((x, i) => new SapDocumentLine(i, x.Item, $"Artículo {x.Item}", x.Qty, "UN", x.Kg)).ToList());

    private static SapDocumentLines Muchas(int desde, int cantidad) =>
        Lineas(Enumerable.Range(desde, cantidad).Select(i => ($"A{i}", 1m, (decimal?)1m)).ToArray());

    private static GuidePlan Plan(Manifest m, params (ManifestDocument Doc, SapDocumentLines Lines)[] docs) =>
        GuidePlanner.Plan(new GuidePlanInput(m, docs, id => id switch { Talca => "Talca", Curico => "Curicó", _ => null },
            TransferIndicator: 1, DispatchType: 2, IssueDate: Hoy, DepartureTime: new TimeOnly(12, 30)));

    [Fact]
    public void Plan_DosFacturasMismoDestino_UnaGuiaConDosReferencias()
    {
        var a = Factura(100);
        var b = Factura(101, dir: "  av. uno   100 ");
        var p = Plan(Manifiesto(), (a, Lineas(("X", 1, 1))), (b, Lineas(("Y", 2, 1))));

        Assert.Empty(p.Errors);
        var g = Assert.Single(p.Guides);
        Assert.Equal(1, g.Sequence);
        Assert.Equal(new[] { a.Id, b.Id }, g.ManifestDocumentIds);
        Assert.Equal(new long?[] { 100, 101 }, g.Document.References.Select(r => r.Folio));
        Assert.All(g.Document.References, r => Assert.Equal("33", r.DocumentType));
        Assert.Equal(new[] { 1, 2 }, g.Document.References.Select(r => r.LineNumber));
        Assert.Contains("manifiesto 15", g.Document.References[0].Reason);
    }

    [Fact]
    public void Plan_DestinosDistintos_UnaGuiaPorDestino()
    {
        var p = Plan(Manifiesto(),
            (Factura(100), Lineas(("X", 1, 1))),
            (Factura(101, comuna: Curico), Lineas(("X", 1, 1))),
            (Factura(102, rut: "96806980-2"), Lineas(("X", 1, 1))),
            (Factura(103), Lineas(("X", 1, 1))));

        Assert.Equal(3, p.Guides.Count);
        Assert.Equal(new[] { 1, 2, 3 }, p.Guides.Select(g => g.Sequence));
        Assert.Equal(2, p.Guides[0].ManifestDocumentIds.Count);
        Assert.Equal("Curicó", p.Guides[1].Document.Receiver.Commune);
    }

    private static SapDocumentLines LineasModelo(bool conNombre, params (string Item, string? Model, decimal Qty, decimal? Kg)[] items) =>
        new("Venta al por menor", "Talca", items.Select((x, i) => new SapDocumentLine(
            i, x.Item, $"Artículo {x.Item}", x.Qty, "UN", x.Kg, x.Model,
            conNombre && x.Model is not null ? $"Modelo {x.Model}" : null)).ToList());

    private static GuidePlan PlanAgrupado(string grouping, params (ManifestDocument Doc, SapDocumentLines Lines)[] docs) =>
        GuidePlanner.Plan(new GuidePlanInput(Manifiesto(), docs, id => id switch { Talca => "Talca", Curico => "Curicó", _ => null },
            TransferIndicator: 1, DispatchType: 2, IssueDate: Hoy, DepartureTime: new TimeOnly(12, 30), LineGrouping: grouping));

    [Fact]
    public void Plan_PorModelo_SumaLasTallasEnUnaLinea_YLoSinModeloQuedaPorSku()
    {
        var p = PlanAgrupado(GuideLineGroupings.Model, (Factura(100), LineasModelo(true,
            ("761952C-805-7", "761952C-805", 2, 1m), ("761952C-805-9", "761952C-805", 3, 1.5m), ("SINMOD", null, 1, 0.5m))));

        var lines = Assert.Single(p.Guides).Document.Lines;
        Assert.Equal(2, lines.Count);
        Assert.Equal(("761952C-805", "Modelo 761952C-805", 5m), (lines[0].ItemCode, lines[0].Name, lines[0].Quantity));
        Assert.Equal(("SINMOD", 1m), (lines[1].ItemCode, lines[1].Quantity));
        Assert.Equal(3m, p.Guides[0].Document.Transport!.Customs!.NetWeightKg);
    }

    [Fact]
    public void Plan_PorModelo_DosFacturasMismoDestino_SeJuntanPorModelo()
    {
        var p = PlanAgrupado(GuideLineGroupings.Model,
            (Factura(100), LineasModelo(true, ("761952C-805-7", "761952C-805", 2, 1m))),
            (Factura(101), LineasModelo(true, ("761952C-805-8", "761952C-805", 4, 1m))));

        var guide = Assert.Single(p.Guides);
        var line = Assert.Single(guide.Document.Lines);
        Assert.Equal(6m, line.Quantity);
        Assert.Equal(2, guide.Document.References!.Count);
    }

    [Fact]
    public void Plan_PorModelo_SinNombreDeModelo_UsaElMayorNombre()
    {
        var p = PlanAgrupado(GuideLineGroupings.Model, (Factura(100), LineasModelo(false,
            ("761952C-805-7", "761952C-805", 2, 1m), ("761952C-805-9", "761952C-805", 3, 1m))));

        Assert.Equal("Artículo 761952C-805-9", Assert.Single(p.Guides).Document.Lines[0].Name);
    }

    [Fact]
    public void Plan_CodigoEnElDetalle_NoInformaCdgItem_YAnteponeElCodigoAlNombre()
    {
        var p = GuidePlanner.Plan(new GuidePlanInput(Manifiesto(), new[] { (Factura(100), Lineas(("CNVFA23MJOG2-001", 2, 1m))) },
            id => id switch { Talca => "Talca", _ => null }, TransferIndicator: 1, DispatchType: 2, IssueDate: Hoy,
            DepartureTime: new TimeOnly(12, 30), ItemCodePlacement: GuideItemCodePlacements.Name));

        var line = Assert.Single(Assert.Single(p.Guides).Document.Lines);
        Assert.Null(line.ItemCode);
        Assert.Equal("CNVFA23MJOG2-001 Artículo CNVFA23MJOG2-001", line.Name);
    }

    [Fact]
    public void Plan_PorModelo_SinNombreDeModelo_UsaElMaximoDeLosNombres_SinImportarElOrden()
    {
        var a = PlanAgrupado(GuideLineGroupings.Model, (Factura(100), LineasModelo(false,
            ("761952C-805-9", "761952C-805", 1, 1m), ("761952C-805-7", "761952C-805", 1, 1m))));
        var b = PlanAgrupado(GuideLineGroupings.Model,
            (Factura(100), LineasModelo(false, ("761952C-805-7", "761952C-805", 1, 1m))),
            (Factura(101), LineasModelo(false, ("761952C-805-9", "761952C-805", 1, 1m))));

        Assert.Equal("Artículo 761952C-805-9", Assert.Single(Assert.Single(a.Guides).Document.Lines).Name);
        Assert.Equal("Artículo 761952C-805-9", Assert.Single(Assert.Single(b.Guides).Document.Lines).Name);
    }

    private static GuidePlan PlanMax(int maxLines, params (ManifestDocument Doc, SapDocumentLines Lines)[] docs) =>
        GuidePlanner.Plan(new GuidePlanInput(Manifiesto(), docs, id => id switch { Talca => "Talca", _ => null },
            TransferIndicator: 1, DispatchType: 2, IssueDate: Hoy, DepartureTime: new TimeOnly(12, 30), MaxLines: maxLines));

    [Fact]
    public void Plan_MaximoDeLineasDeLaCompania_DivideLaFacturaEnGuiasDe27()
    {
        var p = PlanMax(27, (Factura(100, bultos: 5), Muchas(1, 30)));

        Assert.Equal(new[] { 27, 3 }, p.Guides.Select(g => g.Document.Lines.Count));
        Assert.All(p.Guides, g => Assert.Equal(100, Assert.Single(g.Document.References!).Folio));
    }

    [Fact]
    public void Plan_MaximoDeLineasDeLaCompania_DosFacturasQueNoCabenJuntas_VanEnGuiasSeparadas()
    {
        var p = PlanMax(27, (Factura(100), Muchas(1, 20)), (Factura(101), Muchas(100, 20)));

        Assert.Equal(new[] { 20, 20 }, p.Guides.Select(g => g.Document.Lines.Count));
    }

    private static SapDocumentLines LineasMonto(params (string Item, string? Model, decimal Qty, decimal? Amount)[] items) =>
        new("Venta al por menor", "Talca", items.Select((x, i) => new SapDocumentLine(
            i, x.Item, $"Artículo {x.Item}", x.Qty, "UN", 1m, x.Model, null, x.Amount)).ToList());

    private static GuidePlan PlanValorizado(string grouping, params (ManifestDocument Doc, SapDocumentLines Lines)[] docs) =>
        GuidePlanner.Plan(new GuidePlanInput(Manifiesto(), docs, id => id switch { Talca => "Talca", _ => null },
            TransferIndicator: 1, DispatchType: 2, IssueDate: Hoy, DepartureTime: new TimeOnly(12, 30),
            LineGrouping: grouping, Valued: true));

    [Fact]
    public void Plan_Valorizada_PrecioYMontoDesdeLaFactura()
    {
        var p = PlanValorizado(GuideLineGroupings.Sku, (Factura(100), LineasMonto(("A", null, 3, 30000m), ("B", null, 2, 15001m))));

        var lines = Assert.Single(p.Guides).Document.Lines;
        Assert.Equal((10000m, 30000m, false), (lines[0].UnitPrice!.Value, lines[0].Amount, lines[0].Exempt));
        Assert.Equal((7500.5m, 15001m), (lines[1].UnitPrice!.Value, lines[1].Amount));
    }

    [Fact]
    public void Plan_Valorizada_PorModelo_SumaMontos_YPrecioPromedio()
    {
        var p = PlanValorizado(GuideLineGroupings.Model,
            (Factura(100), LineasMonto(("M-7", "M", 2, 20000m))),
            (Factura(101), LineasMonto(("M-9", "M", 1, 13000m))));

        var line = Assert.Single(Assert.Single(p.Guides).Document.Lines);
        Assert.Equal(33000m, line.Amount);
        Assert.Equal(11000m, line.UnitPrice);
        Assert.Equal(line.Amount, Math.Round(line.Quantity * line.UnitPrice!.Value, 0, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Plan_Valorizada_FacturaExenta_LineasExentasSinMezclarseConAfectas()
    {
        var exenta = Factura(101);
        exenta.SiiDocumentType = 34;
        var p = PlanValorizado(GuideLineGroupings.Sku,
            (Factura(100), LineasMonto(("A", null, 1, 1000m))),
            (exenta, LineasMonto(("A", null, 1, 500m))));

        var lines = Assert.Single(p.Guides).Document.Lines;
        Assert.Equal(2, lines.Count);
        Assert.Equal((false, 1000m), (lines[0].Exempt, lines[0].Amount));
        Assert.Equal((true, 500m), (lines[1].Exempt, lines[1].Amount));
    }

    [Fact]
    public void Plan_Valorizada_SinMontoEnLaConsulta_Error()
    {
        var p = PlanValorizado(GuideLineGroupings.Sku, (Factura(100), LineasMonto(("A", null, 1, null))));

        Assert.Contains(p.Errors, e => e.Contains("Folio 100") && e.Contains("line_amount"));
    }

    [Fact]
    public void Plan_SinValorizar_MontosCero()
    {
        var p = PlanAgrupado(GuideLineGroupings.Sku, (Factura(100), LineasMonto(("A", null, 1, 1000m))));

        var line = Assert.Single(Assert.Single(p.Guides).Document.Lines);
        Assert.Equal((null, 0m), (line.UnitPrice, line.Amount));
    }

    private static readonly DteReceiver Propia = new("76030680-0", "COMERCIAL DEPOR LIMITADA", "Comercio de vestuario", null, null, null);

    private static ManifestDocument Transferencia(int docEntry, string bodega, string dir = "Estado 387", long? comuna = Talca) => new()
    {
        Id = Guid.NewGuid(), DocumentKind = DocumentKinds.Transfer, SiiDocumentType = 0, Folio = docEntry + 1000, SapDocEntry = docEntry,
        DocumentDate = Hoy.AddDays(-1), CustomerCode = bodega, CustomerName = "Tienda " + bodega, CustomerTaxId = "76030680-0",
        DestinationAddress = dir, DestinationCommuneId = comuna, Packages = 1, Lpn = "CTBO" + docEntry,
    };

    private static GuidePlan PlanTraslado(DteReceiver? propia, params (ManifestDocument Doc, SapDocumentLines Lines)[] docs) =>
        GuidePlanner.Plan(new GuidePlanInput(Manifiesto(), docs, id => id switch { Talca => "Talca", Curico => "Curicó", _ => null },
            TransferIndicator: 1, DispatchType: 2, IssueDate: Hoy, DepartureTime: new TimeOnly(12, 30), Valued: true,
            OwnCompany: propia, InternalTransferIndicator: 5, InternalDispatchType: 3));

    [Fact]
    public void Plan_Transferencias_GuiaPorSucursal_IndTraslado5_ReceptorPropio_SinReferencias_EnCero()
    {
        var p = PlanTraslado(Propia,
            (Transferencia(501, "TDA01"), LineasMonto(("A", null, 2, 1000m))),
            (Transferencia(502, "TDA01"), LineasMonto(("B", null, 1, 500m))),
            (Transferencia(503, "TDA02", "Merced 10", Curico), LineasMonto(("A", null, 3, 900m))));

        Assert.Empty(p.Errors);
        Assert.Equal(2, p.Guides.Count);
        var g = p.Guides[0];
        Assert.Equal(2, g.ManifestDocumentIds.Count);
        Assert.Equal((5, (int?)3), (g.Document.IdDoc.TransferIndicator, g.Document.IdDoc.DispatchType));
        Assert.Equal(("76030680-0", "COMERCIAL DEPOR LIMITADA", "Comercio de vestuario"),
            (g.Document.Receiver.TaxId, g.Document.Receiver.Name, g.Document.Receiver.Activity));
        Assert.Equal(("Estado 387", "Talca"), (g.Document.Receiver.Address, g.Document.Receiver.Commune));
        Assert.Empty(g.Document.References);
        Assert.All(g.Document.Lines, l => { Assert.Null(l.UnitPrice); Assert.Equal(0m, l.Amount); Assert.False(l.Exempt); });
        Assert.Equal(2, g.Document.Transport!.Customs!.TotalPackages);
        Assert.Equal("Curicó", p.Guides[1].Document.Receiver.Commune);
    }

    [Fact]
    public void Plan_Mixto_FacturasYTransferencias_GuiasSeparadas()
    {
        var p = PlanTraslado(Propia,
            (Factura(100), LineasMonto(("A", null, 1, 1000m))),
            (Transferencia(501, "TDA01", "Av. Uno 100"), LineasMonto(("A", null, 2, 2000m))));

        Assert.Equal(2, p.Guides.Count);
        var factura = p.Guides.Single(g => g.Document.IdDoc.TransferIndicator == 1);
        var traslado = p.Guides.Single(g => g.Document.IdDoc.TransferIndicator == 5);
        Assert.Single(factura.Document.References);
        Assert.Equal(1000m, Assert.Single(factura.Document.Lines).Amount);
        Assert.Empty(traslado.Document.References);
        Assert.Equal(0m, Assert.Single(traslado.Document.Lines).Amount);
    }

    [Fact]
    public void Plan_GuiaDelAddon_SeOmite()
    {
        var p = PlanTraslado(Propia,
            (Factura(100), LineasMonto(("A", null, 1, 1000m))),
            (Factura(900, kind: DocumentKinds.TransferGuide), new SapDocumentLines(null, null, Array.Empty<SapDocumentLine>())));

        Assert.Empty(p.Errors);
        Assert.Single(Assert.Single(p.Guides).ManifestDocumentIds);
    }

    [Fact]
    public void Plan_TransferenciaSinEmpresaPropia_Error()
    {
        var p = PlanTraslado(null, (Transferencia(501, "TDA01"), LineasMonto(("A", null, 2, 1000m))));

        Assert.Contains(p.Errors, e => e.Contains("razón social y giro"));
        Assert.Empty(p.Guides);
    }

    [Fact]
    public void Plan_PorSku_NoJuntaLasTallas()
    {
        var p = PlanAgrupado(GuideLineGroupings.Sku, (Factura(100), LineasModelo(true,
            ("761952C-805-7", "761952C-805", 2, 1m), ("761952C-805-9", "761952C-805", 3, 1m))));

        Assert.Equal(new[] { "761952C-805-7", "761952C-805-9" }, Assert.Single(p.Guides).Document.Lines.Select(l => l.ItemCode));
    }

    [Fact]
    public void Plan_ConsolidaPorArticulo()
    {
        var p = Plan(Manifiesto(),
            (Factura(100), Lineas(("X", 2, 1.5m), ("Y", 1, 1))),
            (Factura(101), Lineas(("X", 3, 2), ("X", 1, 0.5m))));

        var lines = Assert.Single(p.Guides).Document.Lines;
        Assert.Equal(2, lines.Count);
        Assert.Equal("X", lines[0].ItemCode);
        Assert.Equal(6, lines[0].Quantity);
        Assert.Equal(new[] { 1, 2 }, lines.Select(l => l.LineNumber));
        Assert.All(lines, l => { Assert.Null(l.UnitPrice); Assert.Equal(0, l.Amount); });
    }

    [Fact]
    public void Plan_MasDe60Articulos_Divide()
    {
        var p = Plan(Manifiesto(), (Factura(100), Muchas(1, 35)), (Factura(101), Muchas(100, 35)));

        Assert.Equal(2, p.Guides.Count);
        Assert.All(p.Guides, g => Assert.True(g.Document.Lines.Count <= 60));
        Assert.Equal(100, p.Guides[0].Document.References.Single().Folio);
        Assert.Equal(101, p.Guides[1].Document.References.Single().Folio);
    }

    [Fact]
    public void Plan_FacturaSola_Con130Articulos_TresGuiasQueLaReferencian()
    {
        var f = Factura(100, bultos: 7);
        var p = Plan(Manifiesto(), (f, Muchas(1, 130)));

        Assert.Equal(3, p.Guides.Count);
        Assert.Equal(new[] { 60, 60, 10 }, p.Guides.Select(g => g.Document.Lines.Count));
        Assert.All(p.Guides, g => Assert.Equal(100, g.Document.References.Single().Folio));
        Assert.All(p.Guides, g => Assert.Equal(f.Id, g.ManifestDocumentIds.Single()));
        Assert.Equal(7, p.Guides[0].Document.Transport!.Customs!.TotalPackages);
        Assert.Equal(0, p.Guides[1].Document.Transport!.Customs!.TotalPackages ?? 0);
    }

    [Fact]
    public void Plan_MasDe40Facturas_Divide()
    {
        var docs = Enumerable.Range(1, 41).Select(i => (Factura(i), Lineas(("X", 1, 1)))).ToArray();
        var p = Plan(Manifiesto(), docs);
        Assert.Equal(new[] { 40, 1 }, p.Guides.Select(g => g.Document.References.Count));
    }

    [Fact]
    public void Plan_SinFechaLlegada_Error()
    {
        var m = Manifiesto();
        m.EstimatedArrivalDate = null;
        var p = Plan(m, (Factura(100), Lineas(("X", 1, 1))));
        Assert.Contains(p.Errors, e => e.Contains("fecha de llegada estimada"));
    }

    [Fact]
    public void Plan_ComunaNoResuelta_Error()
    {
        var p = Plan(Manifiesto(), (Factura(100, comuna: null), Lineas(("X", 1, 1))));
        Assert.Contains(p.Errors, e => e.Contains("Folio 100") && e.Contains("comuna"));
    }

    [Fact]
    public void Plan_SinLineas_Error()
    {
        var p = Plan(Manifiesto(), (Factura(100), Lineas()));
        Assert.Contains(p.Errors, e => e.Contains("Folio 100") && e.Contains("líneas"));
    }

    [Fact]
    public void Plan_RutClienteInvalido_Error()
    {
        var p = Plan(Manifiesto(), (Factura(100, rut: "1-1"), Lineas(("X", 1, 1))));
        Assert.Contains(p.Errors, e => e.Contains("Folio 100") && e.Contains("RUT"));
    }

    [Fact]
    public void Plan_PesoParcial_SinAduanaPesoYAdvertencia()
    {
        var p = Plan(Manifiesto(), (Factura(100), Lineas(("X", 1, 2), ("Y", 1, null))));
        var g = Assert.Single(p.Guides);
        Assert.Null(g.Document.Transport!.Customs!.NetWeightKg);
        Assert.Contains(p.Warnings, w => w.Contains("1 artículo"));
    }

    [Fact]
    public void Plan_PesoCompleto_SumaPeso()
    {
        var p = Plan(Manifiesto(), (Factura(100), Lineas(("X", 1, 2.5m), ("Y", 1, 1))));
        Assert.Equal(3.5m, Assert.Single(p.Guides).Document.Transport!.Customs!.NetWeightKg);
        Assert.Empty(p.Warnings);
    }

    [Fact]
    public void Plan_TransporteDesdeSnapshot()
    {
        var p = Plan(Manifiesto(llegada: Hoy.AddDays(1)), (Factura(100), Lineas(("X", 1, 1))));
        var d = Assert.Single(p.Guides).Document;
        var t = d.Transport!;
        Assert.Equal("ABCD12", t.Plate);
        Assert.Null(t.TrailerPlate);
        Assert.Equal("76030680-0", t.CarrierTaxId);
        Assert.Equal(("18504907-8", "Eduardo Escobar"), (t.Driver!.TaxId, t.Driver.Name));
        Assert.Equal("Av. Uno 100", t.DestinationAddress);
        Assert.Equal("Talca", t.DestinationCommune);
        Assert.Equal("Talca", t.DestinationCity);
        Assert.Equal((Hoy, new TimeOnly(12, 30), Hoy.AddDays(1)), (t.DepartureDate!.Value, t.DepartureTime!.Value, t.ArrivalDate!.Value));
        Assert.Equal(52, d.IdDoc.DteType);
        Assert.Equal((1, 2), (d.IdDoc.TransferIndicator!.Value, d.IdDoc.DispatchType!.Value));
        Assert.Equal("Camino San Esteban 1361", d.Issuer!.OriginAddress);
        Assert.Equal("San Bernardo", d.Issuer.OriginCommune);
        Assert.Equal("Venta al por menor", d.Receiver.Activity);
        Assert.Equal("76086428-5", d.Receiver.TaxId);
    }

    [Fact]
    public void Plan_GuiaManual_NoGeneraGuiaNiError()
    {
        var manual = Factura(0, kind: DocumentKinds.ManualGuide);
        var p = Plan(Manifiesto(), (Factura(100), Lineas(("X", 1, 1))), (manual, new SapDocumentLines(null, null, Array.Empty<SapDocumentLine>())));
        Assert.Empty(p.Errors);
        var g = Assert.Single(p.Guides);
        Assert.DoesNotContain(manual.Id, g.ManifestDocumentIds);
    }
}
