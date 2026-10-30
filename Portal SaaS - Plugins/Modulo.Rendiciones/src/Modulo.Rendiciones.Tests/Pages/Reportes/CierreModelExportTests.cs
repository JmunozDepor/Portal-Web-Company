using System.Text;
using Microsoft.AspNetCore.Mvc;
using Modulo.Rendiciones.Pages.Reportes;
using Modulo.Rendiciones.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Tests.Pages.Reportes;

/// <summary>
/// Task 9 (fix de alcance): el CSV de cierre debe mostrar tanto el monto de la boleta
/// (Amount) como el monto efectivamente reembolsado/contabilizado (ReimbursementAmount,
/// Task 7) -- nunca ocultar uno a costa del otro (Decisión 7 del diseño). Antes de este
/// fix, el export solo escribía Amount en una columna "Monto" y no existía
/// "MontoReembolso".
/// </summary>
public class CierreModelExportTests
{
    private sealed class StubClosingReportService : IClosingReportService
    {
        public IReadOnlyList<ClosingReportLine> Lines { get; set; } = Array.Empty<ClosingReportLine>();

        public Task<IReadOnlyList<ClosingReportLine>> GenerateAsync(Guid companyId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
            Task.FromResult(Lines);
    }

    private sealed class FakeCurrentCompany : ICurrentCompanyAccessor
    {
        public Guid CompanyId { get; } = Guid.NewGuid();
        public string Code => "TEST";
        public string Database => "test";
        public string ServiceLayerUrl => "";
        public string Country => "CL";
        public bool HasCompany => true;
    }

    [Fact]
    public async Task OnGetExportarAsync_CsvIncludesBothAmountAndReimbursementAmount()
    {
        var line = new ClosingReportLine(
            ExpenseReportId: 1,
            Round: 1,
            EmployeeName: "Juan Pérez",
            CostCenterCode: "CC1",
            CostCenterName: "Ventas",
            ExpenseTypeName: "Alojamiento",
            Date: new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            Amount: 10000m,
            Currency: "CLP",
            Notes: "Viaje",
            ReimbursementAmount: 6000m);

        var sut = new CierreModel(new StubClosingReportService { Lines = new[] { line } }, new FakeCurrentCompany());

        var result = await sut.OnGetExportarAsync(CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        var csv = Encoding.UTF8.GetString(file.FileContents);

        var headerLine = csv.Split('\n')[0];
        Assert.Contains("Monto", headerLine);
        Assert.Contains("MontoReembolso", headerLine);

        var dataLine = csv.Split('\n')[1];
        Assert.Contains("10000.00", dataLine);
        Assert.Contains("6000.00", dataLine);
    }
}
