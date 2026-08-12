using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public interface IExpenseApprovalGroupService
{
    Task<IReadOnlyList<ExpenseApprovalGroup>> ListAsync(Guid companyId, CancellationToken ct = default);

    Task<ExpenseApprovalGroup?> GetAsync(long id, Guid companyId, CancellationToken ct = default);

    /// <summary>Se asume 1 grupo por usuario -- null si no pertenece a ninguno (autoaprueba al enviar).</summary>
    Task<ExpenseApprovalGroup?> GetUserGroupAsync(Guid companyId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<ExpenseApprovalGroupMember>> ListMembersAsync(long groupId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<int, Guid>> GetLevelsAsync(long groupId, CancellationToken ct = default);

    Task<long> CreateAsync(Guid companyId, string name, CancellationToken ct = default);

    Task AddMemberAsync(long groupId, Guid companyId, Guid userId, CancellationToken ct = default);

    Task RemoveMemberAsync(long groupId, Guid companyId, Guid userId, CancellationToken ct = default);

    Task SetLevelAsync(long groupId, Guid companyId, int level, Guid userId, CancellationToken ct = default);

    Task RemoveLevelAsync(long groupId, Guid companyId, int level, CancellationToken ct = default);

    /// <summary>
    /// Busca desde levelFrom el primer nivel cuyo aprobador NO sea el propio
    /// solicitante -- salto de nivel si aprobador=solicitante, null si se agotan los
    /// niveles (autoaprobación).
    /// </summary>
    static int? ResolveNextLevel(IReadOnlyDictionary<int, Guid> approverByLevel, int levelFrom, Guid requesterUserId)
    {
        var maxLevel = approverByLevel.Keys.DefaultIfEmpty(0).Max();
        for (var level = levelFrom; level <= maxLevel; level++)
        {
            if (approverByLevel.TryGetValue(level, out var approverId) && approverId != requesterUserId)
                return level;
        }

        return null;
    }
}
