using Modulo.FacturasProveedor.Pages;

namespace Modulo.FacturasProveedor.Tests.Pages;

public class FacturasProveedorUiTests
{
    [Theory]
    [InlineData(33, "Factura electrónica")]
    [InlineData(34, "Factura exenta electrónica")]
    [InlineData(43, "Liquidación factura electrónica")]
    [InlineData(56, "Nota de débito electrónica")]
    [InlineData(61, "Nota de crédito electrónica")]
    [InlineData(99, "DTE 99")]
    public void DocumentTypeName(int dteType, string expected) =>
        Assert.Equal(expected, FacturasProveedorUi.DocumentTypeName(dteType));

    [Theory]
    [InlineData("CONTADO", "Contado")]
    [InlineData("CREDITO", "Crédito")]
    [InlineData("SIN_COSTO", "Sin costo")]
    [InlineData("OTRA_FORMA", "OTRA_FORMA")]
    [InlineData(null, "—")]
    public void PaymentFormLabel(string? form, string expected) =>
        Assert.Equal(expected, FacturasProveedorUi.PaymentFormLabel(form));

    [Theory]
    [InlineData("33", "Factura electrónica")]
    [InlineData("801", "Orden de compra")]
    [InlineData("HES", "HES")]
    public void ReferenceTypeName(string type, string expected) =>
        Assert.Equal(expected, FacturasProveedorUi.ReferenceTypeName(type));

    [Theory]
    [InlineData(1, "Anula documento")]
    [InlineData(2, "Corrige texto")]
    [InlineData(3, "Corrige montos")]
    [InlineData(null, "—")]
    public void ReferenceCodeLabel(int? code, string expected) =>
        Assert.Equal(expected, FacturasProveedorUi.ReferenceCodeLabel(code));

    /// <summary>Registro SII 28-10-2026 12:14:31 hora de Chile (UTC-3).</summary>
    private static readonly DateTimeOffset Registered = new(2026, 10, 28, 15, 14, 31, TimeSpan.Zero);

    [Fact]
    public void DeadlineTooltip_EnPlazo_DiasHorasYMinutosDesdeElRegistro()
    {
        var now = Registered.AddHours(2).AddMinutes(8);

        Assert.Equal("Registro SII: 28-10-2026 12:14 · Plazo para responder: 7 días, 21 horas y 52 minutos",
            FacturasProveedorUi.DeadlineTooltip(Registered, new DateOnly(2026, 11, 5), 8, true, now));
    }

    [Fact]
    public void DeadlineTooltip_Singulares()
    {
        var now = Registered.AddDays(8).AddDays(-1).AddHours(-1).AddMinutes(-1);

        Assert.EndsWith("Plazo para responder: 1 día, 1 hora y 1 minuto",
            FacturasProveedorUi.DeadlineTooltip(Registered, new DateOnly(2026, 11, 5), 8, true, now));
    }

    [Fact]
    public void DeadlineTooltip_Vencido()
    {
        Assert.Equal("Registro SII: 28-10-2026 12:14 · Plazo vencido el 05-11-2026 12:14",
            FacturasProveedorUi.DeadlineTooltip(Registered, new DateOnly(2026, 11, 5), 8, true, Registered.AddDays(9)));
    }

    [Fact]
    public void DeadlineTooltip_NoRespondible_SoloElRegistro()
    {
        Assert.Equal("Registro SII: 28-10-2026 12:14",
            FacturasProveedorUi.DeadlineTooltip(Registered, new DateOnly(2026, 11, 5), 8, false, Registered));
    }

    [Fact]
    public void DeadlineTooltip_SinHoraDeRegistro_UsaElVencimiento()
    {
        Assert.Equal("Vence el 05-11-2026",
            FacturasProveedorUi.DeadlineTooltip(null, new DateOnly(2026, 11, 5), 8, true, Registered));
    }

    [Fact]
    public void RegisteredLabel_HoraDeChile_OFechaSiNoHayHora()
    {
        Assert.Equal("28-10-2026 12:14", FacturasProveedorUi.RegisteredLabel(Registered, new DateOnly(2026, 10, 28)));
        Assert.Equal("28-10-2026", FacturasProveedorUi.RegisteredLabel(null, new DateOnly(2026, 10, 28)));
    }
}
