using Modulo.FacturasProveedor.Dominio;

namespace Modulo.FacturasProveedor.Tests.Dominio;

public class ReviewDeadlinesTests
{
    [Fact]
    public void LegalDeadline_Recibida20Oct_Plazo8_28Oct() =>
        Assert.Equal(new DateOnly(2026, 10, 28), ReviewDeadlines.LegalDeadline(new DateOnly(2026, 10, 20), 8));

    [Fact]
    public void WarnDeadline_NuncaLlegaAlUltimoDia()
    {
        var received = new DateOnly(2026, 10, 20);
        var legal = ReviewDeadlines.LegalDeadline(received, 8);

        Assert.Equal(new DateOnly(2026, 10, 23), ReviewDeadlines.WarnDeadline(received, 3, legal));
        Assert.Equal(new DateOnly(2026, 10, 27), ReviewDeadlines.WarnDeadline(received, 10, legal));
    }

    [Fact]
    public void IsDue_MismoDiaAntesDelCorte_False_DespuesTrue()
    {
        var due = new DateOnly(2026, 10, 28);
        var cutoff = new TimeOnly(10, 0);

        Assert.False(ReviewDeadlines.IsDue(due, cutoff, new DateTime(2026, 10, 28, 9, 59, 0)));
        Assert.True(ReviewDeadlines.IsDue(due, cutoff, new DateTime(2026, 10, 28, 10, 0, 0)));
        Assert.True(ReviewDeadlines.IsDue(due, cutoff, new DateTime(2026, 10, 29, 0, 1, 0)));
        Assert.False(ReviewDeadlines.IsDue(due, cutoff, new DateTime(2026, 10, 27, 23, 0, 0)));
    }

    [Fact]
    public void ShouldEscalate_DosDiasAntes()
    {
        var legal = new DateOnly(2026, 10, 28);
        Assert.False(ReviewDeadlines.ShouldEscalate(legal, 2, new DateTime(2026, 10, 25, 12, 0, 0)));
        Assert.True(ReviewDeadlines.ShouldEscalate(legal, 2, new DateTime(2026, 10, 26, 0, 0, 0)));
    }
}

public class OcBalanceCalculatorTests
{
    [Fact]
    public void AlreadyInvoiced_DeduplicaPortalYSap()
    {
        var portal = new[] { new InvoicedAgainstOc("76.412.553-1", 18101, 1_000_000), new InvoicedAgainstOc("76412553-1", 18300, 500_000) };
        var sap = new[] { new InvoicedAgainstOc("76412553-1", 18101, 999_000), new InvoicedAgainstOc("76412553-1", 17000, 200_000) };

        Assert.Equal(1_700_000, OcBalanceCalculator.AlreadyInvoiced(portal, sap));
    }

    [Fact]
    public void AlreadyInvoiced_SinNada_Cero() =>
        Assert.Equal(0, OcBalanceCalculator.AlreadyInvoiced(Array.Empty<InvoicedAgainstOc>(), Array.Empty<InvoicedAgainstOc>()));
}

public class DueActionPlannerTests
{
    private static readonly DueSettings Settings = new(new TimeOnly(10, 0), 2, true, "reject", 0);
    private static readonly DateOnly Legal = new(2026, 10, 28);

    private static DueAction Plan(string status, DateTime nowChile, DateOnly? warn = null, bool escalated = false, DueSettings? settings = null) =>
        DueActionPlanner.Plan(new DueSnapshot(status, Legal, warn, escalated), settings ?? Settings, nowChile);

    [Fact]
    public void Plan_UltimoDiaDespuesDelCorte_AplicaReglaDePlazo()
    {
        var a = Plan("in_review", new DateTime(2026, 10, 28, 10, 30, 0), escalated: true);
        Assert.Equal(DueActionKind.ApplyDeadlineRule, a.Kind);
        Assert.Equal("reject", a.Action);

        Assert.Equal(DueActionKind.None, Plan("in_review", new DateTime(2026, 10, 28, 9, 0, 0), escalated: true).Kind);
        Assert.Equal(DueActionKind.None, Plan("in_review", new DateTime(2026, 10, 28, 11, 0, 0), escalated: true,
            settings: Settings with { DeadlineRuleActive = false }).Kind);
    }

    [Fact]
    public void Plan_DiasAntes_AdelantaLaRegla()
    {
        var a = Plan("warning", new DateTime(2026, 10, 27, 10, 0, 0), escalated: true, settings: Settings with { DeadlineDaysBefore = 1, DeadlineRuleAction = "accept" });
        Assert.Equal(DueActionKind.ApplyDeadlineRule, a.Kind);
        Assert.Equal("accept", a.Action);
    }

    [Fact]
    public void Plan_PasadoElPlazo_MarcaVencida()
    {
        foreach (var status in new[] { "new", "warning", "in_review", "to_send" })
            Assert.Equal(DueActionKind.MarkExpired, Plan(status, new DateTime(2026, 10, 29, 0, 5, 0)).Kind);
    }

    /// <summary>C3 (revisión final): una "nueva" que no se pudo evaluar (ej. SAP caído) también se escala y toma la regla de plazo.</summary>
    [Fact]
    public void Plan_NuevaSinEvaluar_SeEscalaYTomaLaReglaDePlazo()
    {
        Assert.Equal(DueActionKind.Escalate, Plan("new", new DateTime(2026, 10, 26, 12, 0, 0)).Kind);
        Assert.Equal(DueActionKind.ApplyDeadlineRule, Plan("new", new DateTime(2026, 10, 28, 10, 30, 0), escalated: true).Kind);
    }

    /// <summary>
    /// I3 (revisión final): una simulada sin respuesta del operador vence (la ley la da por aceptada) y, en modo activo, se
    /// escala a Finanzas antes; nunca se le aplica la regla de plazo (no se envía).
    /// </summary>
    [Fact]
    public void Plan_Simulada_Vence_YSeEscalaSoloEnActivo()
    {
        var active = Settings with { EscalateSimulated = true };
        var now = new DateTime(2026, 10, 26, 12, 0, 0);
        Assert.Equal(DueActionKind.MarkExpired, Plan("simulated", new DateTime(2026, 10, 29, 0, 5, 0)).Kind);
        Assert.Equal(DueActionKind.None, Plan("simulated", now).Kind);
        Assert.Equal(DueActionKind.Escalate, Plan("simulated", now, settings: active).Kind);
        Assert.Equal(DueActionKind.None, Plan("simulated", new DateTime(2026, 10, 28, 10, 30, 0), escalated: true, settings: active).Kind);
    }

    [Fact]
    public void Plan_AvisoVencido_Rechaza()
    {
        var a = Plan("warning", new DateTime(2026, 10, 23, 10, 0, 0), warn: new DateOnly(2026, 10, 23));
        Assert.Equal(DueActionKind.RejectWarnExpired, a.Kind);
        Assert.Equal("reject", a.Action);
    }

    [Fact]
    public void Plan_Escalamiento_UnaSolaVez()
    {
        var now = new DateTime(2026, 10, 26, 12, 0, 0);
        Assert.Equal(DueActionKind.Escalate, Plan("in_review", now).Kind);
        Assert.Equal(DueActionKind.None, Plan("in_review", now, escalated: true).Kind);
        Assert.Equal(DueActionKind.None, Plan("to_send", now).Kind);
    }
}
