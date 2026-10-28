using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

/// <summary>Un informe sobre el que el aprobador ya tomó una decisión alguna vez (en
/// cualquier nivel), junto con esa decisión -- el informe puede seguir Pending en un
/// nivel siguiente, o ya estar Approved/Rejected. Ver IExpenseReportService.ListResolvedByApproverAsync.</summary>
public sealed record ResolvedReportForApprover(ExpenseReport Report, ExpenseReportAction MyLastAction);
