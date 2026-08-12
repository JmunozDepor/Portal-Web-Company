namespace Modulo.Rendiciones.Pages;

/// <summary>
/// Helpers puramente visuales para las vistas -- icono por categoría de gasto (clases
/// Bootstrap Icons, ya vendored por el portal en wwwroot/lib/bootstrap-icons/, ver
/// CLAUDE.md del portal "Sidebar dinámico") y color semántico por estado. Sin lógica
/// de negocio acá, solo presentación.
/// </summary>
public static class RendicionesUi
{
    /// <summary>Match por palabra clave sobre el nombre del ExpenseType -- catálogo libre, no hay un código fijo que mapear.</summary>
    public static string CategoryIcon(string? expenseTypeName)
    {
        if (string.IsNullOrWhiteSpace(expenseTypeName))
            return "bi-receipt";

        var n = expenseTypeName.ToLowerInvariant();
        return n switch
        {
            _ when n.Contains("combustible") || n.Contains("bencina") || n.Contains("gasolina") => "bi-fuel-pump",
            _ when n.Contains("aloj") || n.Contains("hotel") => "bi-house-door",
            _ when n.Contains("aliment") || n.Contains("almuerzo") || n.Contains("comida") || n.Contains("cena") => "bi-cup-hot",
            _ when n.Contains("taxi") || n.Contains("uber") || n.Contains("bus") || n.Contains("transporte") => "bi-car-front",
            _ when n.Contains("peaje") || n.Contains("estacionamiento") || n.Contains("parking") => "bi-signpost-split",
            _ when n.Contains("repar") || n.Contains("manten") => "bi-wrench",
            _ when n.Contains("servicio") && n.Contains("oficina") => "bi-building",
            _ when n.Contains("avion") || n.Contains("vuelo") || n.Contains("pasaje") => "bi-airplane",
            _ => "bi-receipt",
        };
    }

    /// <summary>Estado de ExpenseReport (Draft/Pending/Approved/Rejected), ExpenseFund (Open/Settled/Overdue) o ExpenseReportLine (Loose/InReport).</summary>
    public static string BadgeClass(string status) => status switch
    {
        "Approved" or "Settled" => "rnd-badge--success",
        "Pending" or "Open" => "rnd-badge--warning",
        "Rejected" or "Overdue" => "rnd-badge--danger",
        "InReport" => "rnd-badge--accent",
        _ => "rnd-badge--neutral", // Draft, Loose
    };
}
