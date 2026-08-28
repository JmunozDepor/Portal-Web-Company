using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>Función pura, sin dependencias de EF Core, para poder testearla sin base de datos -- ver RendicionesReminderBackgroundService.</summary>
public static class ReminderGrouping
{
    public static IReadOnlyDictionary<Guid, List<ExpenseReport>> GroupPendingByApprover(
        IReadOnlyList<ExpenseReport> pending,
        IReadOnlyDictionary<long, IReadOnlyDictionary<int, Guid>> levelsByGroup)
    {
        var result = new Dictionary<Guid, List<ExpenseReport>>();

        foreach (var report in pending)
        {
            if (report.ExpenseApprovalGroupId is not { } groupId || report.CurrentLevel is not { } level)
                continue;

            if (!levelsByGroup.TryGetValue(groupId, out var levels) || !levels.TryGetValue(level, out var approverId))
                continue;

            if (!result.TryGetValue(approverId, out var list))
            {
                list = new List<ExpenseReport>();
                result[approverId] = list;
            }

            list.Add(report);
        }

        return result;
    }
}
