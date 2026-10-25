using Modulo.Despachos.Dominio.Guias;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.Despachos.Tests.Dominio.Guias;

/// <summary>Documento de la guía manual: IVA según el motivo, observaciones como última línea y transporte Res. 154.</summary>
public class ManualGuideBuilderTests
{
    private static readonly ManualGuideOrigin Origin = new("Avda. La Dehesa 1500 of. 601", "Vitacura", "Santiago");

    private static ManualGuideInput Input(
        IReadOnlyList<ManualGuideLineInput>? lines = null, int dispatchType = 3, string mode = ManualTransportModes.Manual,
        ManualGuideTransportInput? transport = null, string? notes = null, string rut = "17183714-6") =>
        new(new DateOnly(2026, 10, 23), 6, dispatchType, 1,
            rut, "Freddi Fuentes Norambuena", "Tienda Paris Mall Plaza Maule", null,
            "Avenida Circunvalación 1055", "Talca", "Talca",
            "Avenida Circunvalación 1055", "Talca", "Talca",
            lines ?? new[] { new ManualGuideLineInput(null, "Mueble", 1, null, 500000) },
            Array.Empty<ManualGuideReferenceInput>(), notes, mode,
            transport ?? Full);

    /// <summary>Transporte completo Res. 154: vehículo, chofer, transportista y salida/llegada.</summary>
    private static readonly ManualGuideTransportInput Full = new("ABCD12", null, "12345678-5", "Juan Pérez", "76543210-3", "TSM",
        new DateOnly(2026, 10, 23), new TimeOnly(9, 30), new DateOnly(2026, 10, 24));

    [Fact]
    public void Afecto_Mueble_TotalConIva()
    {
        var doc = ManualGuideBuilder.BuildDraft(Input(), Origin, vatAffected: true);
        Assert.Equal((500000m, 95000m, 595000m), ManualGuideBuilder.Totals(doc.Lines));
        Assert.All(doc.Lines, l => Assert.False(l.Exempt));
    }

    [Fact]
    public void NoAfecto_Cajas_LineasExentas_TotalIgualNeto()
    {
        var doc = ManualGuideBuilder.BuildDraft(Input(new[] { new ManualGuideLineInput("ITEM-0", "CAJAS 550x500x345", 30, null, 608) }),
            Origin, vatAffected: false);
        Assert.All(doc.Lines, l => Assert.True(l.Exempt));
        Assert.Equal((18240m, 0m, 18240m), ManualGuideBuilder.Totals(doc.Lines));
        Assert.Equal("ITEM-0", doc.Lines[0].ItemCode);
    }

    [Fact]
    public void BuildDraft_OrigenYDestino()
    {
        var doc = ManualGuideBuilder.BuildDraft(Input(), Origin, vatAffected: true);
        Assert.Equal(new DteIdDoc(52, new DateOnly(2026, 10, 23), 6, 3), doc.IdDoc);
        Assert.Equal("Vitacura", doc.Issuer!.OriginCommune);
        Assert.Equal("17183714-6", doc.Receiver.TaxId);
        Assert.Equal("Talca", doc.Transport!.DestinationCommune);
        Assert.Equal("ABCD12", doc.Transport.Plate);
    }

    [Fact]
    public void Validate_RutInvalido() =>
        Assert.Contains(ManualGuideBuilder.Validate(Input(rut: "12.345.678-0"), 60), e => e.Contains("RUT del receptor"));

    [Fact]
    public void Validate_SinLineas() =>
        Assert.Contains(ManualGuideBuilder.Validate(Input(Array.Empty<ManualGuideLineInput>()), 60),
            e => e.Contains("Agregue al menos una línea"));

    [Fact]
    public void Validate_CantidadCero() =>
        Assert.Contains(ManualGuideBuilder.Validate(Input(new[] { new ManualGuideLineInput(null, "Caja", 0, null, 1) }), 60),
            e => e.Contains("línea 1", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Validate_ExcedeMaximo()
    {
        var lines = Enumerable.Range(1, 27).Select(i => new ManualGuideLineInput(null, "Caja " + i, 1, null, 1)).ToList();
        Assert.Contains(ManualGuideBuilder.Validate(Input(lines, notes: "Entregar en bodega"), 27),
            e => e.Contains("máximo de 27 líneas (incluidas las de observaciones)"));
        Assert.Empty(ManualGuideBuilder.Validate(Input(lines), 27));
    }

    [Fact]
    public void Validate_Despacho2_SinPatente_Error() =>
        Assert.Contains(ManualGuideBuilder.Validate(Input(dispatchType: 2,
                transport: Full with { Plate = null }), 60),
            e => e.Contains("patente"));

    [Fact]
    public void Validate_Despacho1_SinTransporte_Error() =>
        Assert.Contains(ManualGuideBuilder.Validate(Input(dispatchType: 1,
            transport: new ManualGuideTransportInput(null, null, null, null, null, null)), 60), e => e.Contains("patente"));

    [Fact]
    public void Validate_SinFechasDeSalidaYLlegada_Error()
    {
        var errors = ManualGuideBuilder.Validate(Input(transport: Full with { DepartureDate = null, DepartureTime = null, ArrivalDate = null }), 60);
        Assert.Contains(errors, e => e.Contains("fecha y hora de salida"));
        Assert.Contains(errors, e => e.Contains("fecha de llegada"));
    }

    [Fact]
    public void Validate_LlegadaAntesDeLaSalida_Error() =>
        Assert.Contains(ManualGuideBuilder.Validate(Input(transport: Full with { ArrivalDate = new DateOnly(2026, 10, 22) }), 60),
            e => e.Contains("anterior a la salida"));

    [Fact]
    public void BuildDraft_LlevaSalidaYLlegada()
    {
        var t = ManualGuideBuilder.BuildDraft(Input(), Origin, vatAffected: true).Transport!;
        Assert.Equal((new DateOnly(2026, 10, 23), new TimeOnly(9, 30), new DateOnly(2026, 10, 24)), (t.DepartureDate, t.DepartureTime, t.ArrivalDate));
    }

    [Fact]
    public void Validate_ModoManifiesto_NoExigeTransporte() =>
        Assert.Empty(ManualGuideBuilder.Validate(Input(mode: ManualTransportModes.Manifest,
            transport: new ManualGuideTransportInput(null, null, null, null, null, null)), 60));

    [Fact]
    public void Validate_NotasMasDe500() =>
        Assert.Contains(ManualGuideBuilder.Validate(Input(notes: new string('x', 501)), 60), e => e.Contains("500"));

    [Fact]
    public void ForEmission_AgregaLineaObservaciones_AlFinal()
    {
        var draft = ManualGuideBuilder.BuildDraft(Input(), Origin, vatAffected: true);
        var doc = ManualGuideBuilder.ForEmission(draft, "Entregar a Juan, timbre 2", null, new DateOnly(2026, 10, 24));
        var last = doc.Lines[^1];
        Assert.Equal(2, doc.Lines.Count);
        // FACELE imprime NmbItem (no DscItem): el texto va en el nombre del ítem.
        Assert.Equal((2, "OBS: Entregar a Juan, timbre 2", 0m, 1m), (last.LineNumber, last.Name, last.Amount, last.Quantity));
        Assert.Equal(new DateOnly(2026, 10, 24), doc.IdDoc.IssueDate);
        Assert.Equal((500000m, 95000m, 595000m), ManualGuideBuilder.Totals(doc.Lines));
    }

    [Fact]
    public void ForEmission_ObservacionesLargas_UnaLineaPorRenglonYHasta80Caracteres()
    {
        var draft = ManualGuideBuilder.BuildDraft(Input(), Origin, vatAffected: true);
        var notes = string.Join(Environment.NewLine,
            "Se trasladan desde la sucursal X hasta la sucursal Y", "Color de las bolsas negras, " + new string('a', 70), "300 bultos");
        var lines = ManualGuideBuilder.ForEmission(draft, notes, null, new DateOnly(2026, 10, 24)).Lines.Skip(1).ToList();

        Assert.All(lines, l => Assert.True(l.Name.Length <= 80));
        Assert.Equal("OBS: Se trasladan desde la sucursal X hasta la sucursal Y", lines[0].Name);
        Assert.Equal("300 bultos", lines[^1].Name);
        Assert.Equal(4, lines.Count);
        Assert.Equal(4, ManualGuideBuilder.NoteLines(notes).Count);
    }

    [Fact]
    public void Validate_ObservacionesCuentanSusLineas()
    {
        var lines = Enumerable.Range(1, 25).Select(i => new ManualGuideLineInput(null, "Caja " + i, 1, null, 1)).ToList();
        Assert.Contains(ManualGuideBuilder.Validate(Input(lines, notes: string.Join(Environment.NewLine, "uno", "dos", "tres")), 27), e => e.Contains("máximo de 27 líneas"));
    }

    [Fact]
    public void ForEmission_ConTransporte_UsaPatenteDelManifiesto()
    {
        var draft = ManualGuideBuilder.BuildDraft(Input(mode: ManualTransportModes.Manifest,
            transport: new ManualGuideTransportInput(null, null, null, null, null, null)), Origin, vatAffected: true);
        var snapshot = new DteTransport("AB-1234", null, "76543210-3", new DteDriver("12345678-5", "Juan Pérez"),
            null, null, null, null, new DateOnly(2026, 11, 10), new TimeOnly(12, 0), new DateOnly(2026, 11, 10));
        var doc = ManualGuideBuilder.ForEmission(draft, null, snapshot, new DateOnly(2026, 11, 10));
        Assert.Equal("AB-1234", doc.Transport!.Plate);
        Assert.Equal("Avenida Circunvalación 1055", doc.Transport.DestinationAddress);
        Assert.Equal("Talca", doc.Transport.DestinationCommune);
        Assert.Single(doc.Lines);
    }

    [Fact]
    public void BuildDraft_ToInput_IdaYVuelta()
    {
        var input = Input(notes: "Frágil") with
        {
            References = new[] { new ManualGuideReferenceInput("52", 1, new DateOnly(2026, 10, 23), "Guía anterior") },
        };
        var doc = ManualGuideBuilder.BuildDraft(input, Origin, vatAffected: true);
        var row = new TransportGuide
        {
            IssueDate = input.IssueDate, TransferIndicator = 6, DispatchType = 3, OriginId = 1, Notes = "Frágil",
            TransportMode = ManualTransportModes.Manual, ReceiverCardCode = null,
        };
        var back = ManualGuideBuilder.ToInput(doc, row);
        Assert.Equal(input with { Lines = back.Lines, References = back.References }, back);
        Assert.Equal(input.Lines, back.Lines);
        Assert.Equal(input.References, back.References);
    }
}
