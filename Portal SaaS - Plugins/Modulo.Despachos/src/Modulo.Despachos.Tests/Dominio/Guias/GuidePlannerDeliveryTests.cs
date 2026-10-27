using Modulo.Despachos.Dominio.Guias;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Dominio.Guias;

/// <summary>Entregas con guía previa: una guía valorizada por agrupación (OC) con referencia 801.</summary>
public class GuidePlannerDeliveryTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 10);
    private static readonly DateOnly Pedido = new(2026, 11, 5);
    private const long Talca = 1;

    private static Manifest Manifiesto() => new()
    {
        Id = Guid.NewGuid(), Number = 15, Plate = "ABCD12", CarrierTaxId = "76030680-0", DriverTaxId = "18504907-8",
        DriverName = "Eduardo Escobar", OriginAddress = "Camino San Esteban 1361", OriginCommune = "San Bernardo",
        EstimatedArrivalDate = Hoy,
    };

    private static ManifestDocument Entrega(int docEntry, long groupId, string rut = "77261280-K") => new()
    {
        Id = Guid.NewGuid(), DocumentKind = DocumentKinds.Delivery, SiiDocumentType = 0, Folio = docEntry + 1000,
        SapDocEntry = docEntry, DocumentDate = Hoy.AddDays(-1), CustomerCode = "C001", CustomerName = "Falabella",
        CustomerTaxId = rut, DestinationAddress = "Av. Kennedy 9001", DestinationCommuneId = Talca, Packages = 1,
        DispatchGroupId = groupId, CustomerPo = "4500000001",
    };

    private static SapDocumentLines Lineas(params (string Item, decimal Qty, decimal? Amount)[] items) =>
        new("Grandes tiendas", "Talca",
            items.Select((x, i) => new SapDocumentLine(i, x.Item, $"Artículo {x.Item}", x.Qty, "UN", 1m, Amount: x.Amount)).ToList());

    private static readonly Dictionary<long, DeliveryGuideTerms> Terms = new()
    {
        [10] = new DeliveryGuideTerms("EG-000010", 2, 2, 4500000001, Pedido),
        [11] = new DeliveryGuideTerms("EG-000011", 6, 3, 4500000002, Pedido),
    };

    private static GuidePlan Plan(int maxLines, params (ManifestDocument Doc, SapDocumentLines Lines)[] docs) =>
        GuidePlanner.Plan(new GuidePlanInput(Manifiesto(), docs, id => id == Talca ? "Talca" : null,
            TransferIndicator: 1, DispatchType: 1, IssueDate: Hoy, DepartureTime: new TimeOnly(12, 30), MaxLines: maxLines,
            Valued: false, DeliveryTerms: Terms));

    [Fact]
    public void UnaGuiaPorAgrupacion_Valorizada_ConReferencia801()
    {
        var a = Entrega(701, 10);
        var b = Entrega(702, 10);

        var p = Plan(60, (a, Lineas(("A", 2, 20000m))), (b, Lineas(("B", 1, 15000m))));

        Assert.Empty(p.Errors);
        var g = Assert.Single(p.Guides);
        Assert.Equal(new[] { a.Id, b.Id }, g.ManifestDocumentIds);
        Assert.Equal((2, 2), (g.Document.IdDoc.TransferIndicator!.Value, g.Document.IdDoc.DispatchType!.Value));
        Assert.Equal(("77261280-K", "Falabella", "Grandes tiendas"), (g.Document.Receiver.TaxId, g.Document.Receiver.Name, g.Document.Receiver.Activity));
        var r = Assert.Single(g.Document.References);
        Assert.Equal(("801", 4500000001L, Pedido), (r.DocumentType, r.Folio!.Value, r.Date));
        Assert.Equal(new decimal?[] { 10000m, 15000m }, g.Document.Lines.Select(l => l.UnitPrice));
        Assert.Equal(new[] { 20000m, 15000m }, g.Document.Lines.Select(l => l.Amount));
    }

    [Fact]
    public void ExcedeMaxLineas_DivideYRepiteReferencia()
    {
        var p = Plan(2, (Entrega(701, 10), Lineas(("A", 1, 1000m), ("B", 1, 1000m), ("C", 1, 1000m))));

        Assert.Empty(p.Errors);
        Assert.Equal(2, p.Guides.Count);
        Assert.All(p.Guides, g => Assert.Equal(4500000001L, Assert.Single(g.Document.References).Folio));
    }

    [Fact]
    public void SinMonto_Error()
    {
        var p = Plan(60, (Entrega(701, 10), Lineas(("A", 1, null))));

        Assert.Contains(p.Errors, e => e.Contains("Entrega 1701") && e.Contains("line_amount"));
        Assert.Empty(p.Guides);
    }

    [Fact]
    public void SinAgrupacion_Error()
    {
        var p = Plan(60, (Entrega(701, 99), Lineas(("A", 1, 1000m))));
        Assert.Contains("Entrega 1701: falta su agrupación con guía previa.", p.Errors);
    }

    [Fact]
    public void DosAgrupacionesMismoCliente_DosGuias_ConSusCondiciones()
    {
        var p = Plan(60, (Entrega(701, 10), Lineas(("A", 1, 1000m))), (Entrega(703, 11), Lineas(("A", 1, 1000m))));

        Assert.Equal(2, p.Guides.Count);
        Assert.Equal(new int?[] { 2, 6 }, p.Guides.Select(g => g.Document.IdDoc.TransferIndicator));
        Assert.Equal(new long?[] { 4500000001, 4500000002 }, p.Guides.Select(g => g.Document.References.Single().Folio));
    }

    /// <summary>Revisión final: Entregas de una misma OC a direcciones distintas van en guías distintas, cada una con su destino y la 801.</summary>
    [Fact]
    public void MismaOc_DireccionesDistintas_UnaGuiaPorDestino()
    {
        var a = Entrega(701, 10);
        var b = Entrega(702, 10);
        b.DestinationAddress = "Av. Vicuña Mackenna 3000";

        var p = Plan(60, (a, Lineas(("A", 1, 1000m))), (b, Lineas(("B", 1, 1000m))));

        Assert.Empty(p.Errors);
        Assert.Equal(2, p.Guides.Count);
        Assert.Equal(new[] { "Av. Kennedy 9001", "Av. Vicuña Mackenna 3000" }, p.Guides.Select(g => g.Document.Transport!.DestinationAddress));
        Assert.All(p.Guides, g => Assert.Equal(4500000001L, Assert.Single(g.Document.References).Folio));
    }
}
