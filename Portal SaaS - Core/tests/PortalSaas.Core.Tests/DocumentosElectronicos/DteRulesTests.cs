using PortalSaas.Abstractions.Modelos.Dte;
using PortalSaas.Core.DocumentosElectronicos;
using Xunit;

namespace PortalSaas.Core.Tests.DocumentosElectronicos;

public class DteRulesTests
{
    /// <summary>Guía 52 completa según Res. 154 (mismos datos que el ejemplo probado en FACELE caoba).</summary>
    internal static DteDocument Guia52Valida() => new(
        new DteIdDoc(52, new DateOnly(2026, 11, 10), TransferIndicator: 1, DispatchType: 2),
        new DteIssuerOverride("Av. Presidente Frei 1234", "Rancagua", "Rancagua"),
        new DteReceiver("76030680-0", "Cliente de Prueba SpA", "Comercio al por menor", "Calle Uno 123", "Curicó", "Curicó"),
        new DteTransport("ABCD12", null, "76030680-0", new DteDriver("18504907-8", "Eduardo Escobar"),
            "Calle Uno 123", "Curicó", "Curicó", new DteCustoms(12.5m, 3, null),
            new DateOnly(2026, 11, 10), new TimeOnly(12, 30), new DateOnly(2026, 11, 10)),
        null,
        new[] { new DteLine(1, "ART-1", "Zapatilla", null, 2, "PAR", null, 0) },
        new[] { new DteReference(1, "33", 1000412, new DateOnly(2026, 11, 9), null, null) },
        Array.Empty<DteExtraField>());

    private static IReadOnlyList<DteValidationError> Errores(DteDocument d) => DteRules.Validate(d);

    [Fact]
    public void Validate_Guia52Completa_SinErrores() => Assert.Empty(Errores(Guia52Valida()));

    [Fact]
    public void Validate_Guia52SinTransporte_ExigeBloque()
    {
        var d = Guia52Valida() with { Transport = null };
        Assert.Contains(Errores(d), e => e.Field == "Transporte");
    }

    [Theory]
    [InlineData("Patente")]
    [InlineData("RUTTrans")]
    [InlineData("RUTChofer")]
    [InlineData("NombreChofer")]
    [InlineData("DirDest")]
    [InlineData("CmnaDest")]
    [InlineData("FchSalida")]
    [InlineData("HraSalida")]
    [InlineData("FchLlegada")]
    public void Validate_Guia52_CampoTransporteFaltante(string campo)
    {
        var d = Guia52Valida();
        var t = d.Transport!;
        t = campo switch
        {
            "Patente" => t with { Plate = " " },
            "RUTTrans" => t with { CarrierTaxId = null },
            "RUTChofer" => t with { Driver = t.Driver! with { TaxId = "" } },
            "NombreChofer" => t with { Driver = t.Driver! with { Name = "" } },
            "DirDest" => t with { DestinationAddress = null },
            "CmnaDest" => t with { DestinationCommune = null },
            "FchSalida" => t with { DepartureDate = null },
            "HraSalida" => t with { DepartureTime = null },
            "FchLlegada" => t with { ArrivalDate = null },
            _ => throw new ArgumentException(campo),
        };
        var errores = Errores(d with { Transport = t });
        Assert.Single(errores);
        Assert.Equal(campo, errores[0].Field);
    }

    [Fact]
    public void Validate_LlegadaAntesDeSalida_Error()
    {
        var d = Guia52Valida();
        d = d with { Transport = d.Transport! with { ArrivalDate = new DateOnly(2026, 11, 9) } };
        Assert.Contains(Errores(d), e => e.Field == "FchLlegada");
    }

    [Fact]
    public void Validate_RutInvalido_Error()
    {
        var d = Guia52Valida();
        d = d with { Transport = d.Transport! with { CarrierTaxId = "76030680-1" } };
        Assert.Equal("RUTTrans", Assert.Single(Errores(d)).Field);
    }

    [Fact]
    public void Validate_RutReceptorInvalido_Error()
    {
        var d = Guia52Valida();
        d = d with { Receiver = d.Receiver with { TaxId = "1-1" } };
        Assert.Equal("RUTRecep", Assert.Single(Errores(d)).Field);
    }

    [Fact]
    public void Validate_Guia52SinGiroReceptor_Error()
    {
        var d = Guia52Valida();
        d = d with { Receiver = d.Receiver with { Activity = null } };
        Assert.Equal("GiroRecep", Assert.Single(Errores(d)).Field);
    }

    [Fact]
    public void Validate_MasDe60Lineas_Error()
    {
        var lines = Enumerable.Range(1, 61).Select(i => new DteLine(i, $"A{i}", $"Art {i}", null, 1, null, null, 0)).ToList();
        Assert.Contains(Errores(Guia52Valida() with { Lines = lines }), e => e.Field == "Detalle");
    }

    [Fact]
    public void Validate_SinLineas_Error() =>
        Assert.Contains(Errores(Guia52Valida() with { Lines = Array.Empty<DteLine>() }), e => e.Field == "Detalle");

    [Fact]
    public void Validate_LineaCantidadCero_Error()
    {
        var lines = new[] { new DteLine(1, "A", "Art", null, 0, null, null, 0) };
        Assert.Contains(Errores(Guia52Valida() with { Lines = lines }), e => e.Field == "Detalle");
    }

    [Fact]
    public void Validate_MasDe40Referencias_Error()
    {
        var refs = Enumerable.Range(1, 41).Select(i => new DteReference(i, "33", i, new DateOnly(2026, 11, 9), null, null)).ToList();
        Assert.Contains(Errores(Guia52Valida() with { References = refs }), e => e.Field == "Referencia");
    }

    [Fact]
    public void Validate_Guia52SinIndTraslado_Error()
    {
        var d = Guia52Valida();
        d = d with { IdDoc = d.IdDoc with { TransferIndicator = null } };
        Assert.Equal("IndTraslado", Assert.Single(Errores(d)).Field);
    }
}
