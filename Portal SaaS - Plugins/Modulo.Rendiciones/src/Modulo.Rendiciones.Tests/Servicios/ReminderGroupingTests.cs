using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

public class ReminderGroupingTests
{
    [Fact]
    public void GroupPendingByApprover_agrupa_varios_informes_del_mismo_aprobador()
    {
        var approverA = Guid.NewGuid();
        var approverB = Guid.NewGuid();

        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var reports = new List<ExpenseReport>
        {
            new() { Id = 1, CompanyId = companyId, UserId = userId, ExpenseApprovalGroupId = 10, CurrentLevel = 1 },
            new() { Id = 2, CompanyId = companyId, UserId = userId, ExpenseApprovalGroupId = 10, CurrentLevel = 1 },
            new() { Id = 3, CompanyId = companyId, UserId = userId, ExpenseApprovalGroupId = 20, CurrentLevel = 2 },
        };

        var levelsByGroup = new Dictionary<long, IReadOnlyDictionary<int, Guid>>
        {
            [10] = new Dictionary<int, Guid> { [1] = approverA },
            [20] = new Dictionary<int, Guid> { [1] = approverB, [2] = approverB },
        };

        var result = ReminderGrouping.GroupPendingByApprover(reports, levelsByGroup);

        Assert.Equal(2, result.Count);
        Assert.Equal(new long[] { 1, 2 }, result[approverA].Select(r => r.Id).OrderBy(x => x).ToArray());
        Assert.Equal(new long[] { 3 }, result[approverB].Select(r => r.Id).ToArray());
    }

    [Fact]
    public void GroupPendingByApprover_ignora_informes_sin_grupo_o_nivel_resuelto()
    {
        var reports = new List<ExpenseReport>
        {
            new() { Id = 1, CompanyId = Guid.NewGuid(), UserId = Guid.NewGuid(), ExpenseApprovalGroupId = null, CurrentLevel = null },
        };
        var levelsByGroup = new Dictionary<long, IReadOnlyDictionary<int, Guid>>();

        var result = ReminderGrouping.GroupPendingByApprover(reports, levelsByGroup);

        Assert.Empty(result);
    }
}
