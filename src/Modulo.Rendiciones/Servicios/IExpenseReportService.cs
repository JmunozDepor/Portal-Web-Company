using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public interface IExpenseReportService
{
    Task<IReadOnlyList<ExpenseReport>> ListByUserAsync(Guid companyId, Guid userId, CancellationToken ct = default);

    Task<ExpenseReport?> GetAsync(long id, Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Crea el informe (cabecera Draft) y adjunta de una vez los gastos sueltos
    /// indicados (deben ser del mismo usuario/compañía y seguir Loose) -- flujo
    /// principal: "Informes: seleccioná los gastos a exportar". Un informe sin gastos
    /// sirve para armar la cabecera primero y adjuntar después con AttachExpensesAsync.
    /// </summary>
    Task<long> CreateReportAsync(Guid companyId, Guid userId, IReadOnlyList<long> expenseIds, long? expenseFundId,
        string? costCenterCode, string? costCenterName, CancellationToken ct = default);

    Task UpdateHeaderAsync(long reportId, Guid companyId, long? expenseFundId,
        string? costCenterCode, string? costCenterName, CancellationToken ct = default);

    /// <summary>
    /// Adjunta gastos sueltos existentes al informe (Status pasa a InReport). Solo si
    /// el informe sigue Draft. Siempre busca entre los sueltos del DUEÑO DEL INFORME,
    /// no de quien ejecuta la acción -- así un administrador editando el borrador de
    /// otra persona adjunta los gastos correctos.
    /// </summary>
    Task AttachExpensesAsync(long reportId, Guid companyId, IReadOnlyList<long> expenseIds, CancellationToken ct = default);

    /// <summary>Desvincula un gasto del informe -- vuelve a Loose, no se borra. Solo si el informe sigue Draft.</summary>
    Task DetachExpenseAsync(long reportId, long lineId, Guid companyId, CancellationToken ct = default);

    /// <summary>Quita el comprobante adjunto de una línea del informe (se borra el archivo) -- la línea sigue en el informe, solo pierde el adjunto. Solo si el informe sigue Draft.</summary>
    Task RemoveReceiptAsync(long reportId, long lineId, Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Borra el informe (solo Draft). Los gastos incluidos vuelven a Loose -- mismo
    /// criterio que DetachExpenseAsync, no se pierde el gasto ni su comprobante, solo
    /// se elimina la cabecera. Solo quien creó el informe puede eliminarlo.
    /// </summary>
    Task DeleteReportAsync(long reportId, Guid companyId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Draft -> Pending. Exige al menos una línea. Resuelve el grupo de aprobación del
    /// solicitante: sin grupo, o con todos los niveles colapsando en el propio
    /// solicitante, autoaprueba directo (Status=Approved); si no, deja CurrentLevel en
    /// el primer nivel que corresponda.
    /// </summary>
    Task SubmitAsync(long reportId, Guid companyId, CancellationToken ct = default);

    /// <summary>
    /// Registra la decisión en ExpenseReportAction (fuente de verdad) y resuelve el
    /// siguiente nivel; si no queda ninguno, cierra en Approved. Exige que approverUserId
    /// sea el aprobador configurado para el CurrentLevel.
    /// </summary>
    Task ApproveAsync(long reportId, Guid companyId, Guid approverUserId, string? comment, CancellationToken ct = default);

    /// <summary>Registra el rechazo y cierra en Rejected -- no borra nada, ReopenAsync es lo que reabre.</summary>
    Task RejectAsync(long reportId, Guid companyId, Guid approverUserId, string? comment, CancellationToken ct = default);

    /// <summary>
    /// Rejected -> Draft, con Round+1 (un rechazo no cierra el caso, se corrige y se
    /// reenvía). Solo el dueño puede reabrir.
    /// </summary>
    Task ReopenAsync(long reportId, Guid companyId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<ExpenseReportAction>> ListHistoryAsync(long reportId, CancellationToken ct = default);

    /// <summary>Bandeja: informes Pending cuyo CurrentLevel corresponde al aprobador indicado.</summary>
    Task<IReadOnlyList<ExpenseReport>> ListPendingForApproverAsync(Guid companyId, Guid approverUserId, CancellationToken ct = default);
}
