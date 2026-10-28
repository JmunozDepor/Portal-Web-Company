namespace Modulo.FacturasProveedor.Dominio;

/// <summary>Estado de una revisión relevante para los vencimientos.</summary>
public sealed record DueSnapshot(string Status, DateOnly LegalDeadlineOn, DateOnly? WarnDeadlineOn, bool Escalated);

/// <summary>Parámetros de vencimiento: hora de corte, escalamiento y la regla "plazo por vencer" ya resuelta para el proveedor.</summary>
public sealed record DueSettings(TimeOnly Cutoff, int EscalateDaysBefore, bool DeadlineRuleActive, string DeadlineRuleAction, int DeadlineDaysBefore);

public enum DueActionKind { None, RejectWarnExpired, ApplyDeadlineRule, Escalate, MarkExpired }

public sealed record DueAction(DueActionKind Kind, string? Action)
{
    public static DueAction None { get; } = new(DueActionKind.None, null);
}

/// <summary>
/// Qué hacer con una revisión pendiente según el reloj (puro). Prioridad: vencida (pasó el plazo legal sin respuesta:
/// la ley la da por aceptada) &gt; fin del aviso (rechazar) &gt; regla de plazo por vencer &gt; escalamiento &gt; nada.
/// </summary>
public static class DueActionPlanner
{
    public static DueAction Plan(DueSnapshot s, DueSettings settings, DateTime nowChile)
    {
        var pending = s.Status is "warning" or "in_review";

        if (s.Status is "new" or "warning" or "in_review" or "to_send" && DateOnly.FromDateTime(nowChile) > s.LegalDeadlineOn)
            return new DueAction(DueActionKind.MarkExpired, null);

        if (s.Status == "warning" && s.WarnDeadlineOn is { } warn && ReviewDeadlines.IsDue(warn, settings.Cutoff, nowChile))
            return new DueAction(DueActionKind.RejectWarnExpired, "reject");

        if (pending && settings.DeadlineRuleActive
            && ReviewDeadlines.IsDue(s.LegalDeadlineOn.AddDays(-settings.DeadlineDaysBefore), settings.Cutoff, nowChile))
            return new DueAction(DueActionKind.ApplyDeadlineRule, settings.DeadlineRuleAction);

        if (pending && !s.Escalated && ReviewDeadlines.ShouldEscalate(s.LegalDeadlineOn, settings.EscalateDaysBefore, nowChile))
            return new DueAction(DueActionKind.Escalate, null);

        return DueAction.None;
    }
}
