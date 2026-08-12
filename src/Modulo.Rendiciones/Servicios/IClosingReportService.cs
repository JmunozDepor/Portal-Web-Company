namespace Modulo.Rendiciones.Servicios;

public interface IClosingReportService
{
    /// <summary>Rendiciones Approved cuyo ResolvedAt cae en [from, to], una fila por línea de gasto.</summary>
    Task<IReadOnlyList<ClosingReportLine>> GenerateAsync(Guid companyId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}
