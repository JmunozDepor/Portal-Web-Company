using ClosedXML.Excel;
using Modulo.Despachos.Models;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Tests.Servicios;

public class DispatchReportServiceTests
{
    private static async Task<(ManifestTestEnv Env, DispatchReportService Rep)> BuildAsync()
    {
        var env = ManifestTestEnv.Build();
        var mto = await env.Master.SaveCarrierAsync(new Carrier { Code = "MTO", Name = "MTO", TaxId = "76123456-0" });
        await MonthlyClosureServiceTests.SeedManifestAsync(env, 1, new DateOnly(2026, 9, 1), costFinal: 597047, units: 100, docs: 3, packages: 10);
        await MonthlyClosureServiceTests.SeedManifestAsync(env, 2, new DateOnly(2026, 9, 2), costFinal: 2250125, units: 200, docs: 5, packages: 20);
        await MonthlyClosureServiceTests.SeedManifestAsync(env, 3, new DateOnly(2026, 9, 2), costFinal: 385000, carrierId: mto, units: 50, docs: 2, packages: 4);
        await MonthlyClosureServiceTests.SeedManifestAsync(env, 4, new DateOnly(2026, 9, 3), ManifestStatuses.Annulled, costFinal: 999999);
        await MonthlyClosureServiceTests.SeedManifestAsync(env, 5, new DateOnly(2026, 10, 1), costFinal: 777);
        return (env, new DispatchReportService(env.Db, env.Company));
    }

    [Fact]
    public async Task Report_AgrupaPorDiaTurnoTransportista()
    {
        var (_, rep) = await BuildAsync();
        var r = await rep.GetMonthlyAsync(2026, 9);

        Assert.Equal(3232172m, r.TotalCost);
        Assert.Equal(350m, r.TotalUnits);
        Assert.Equal(10, r.TotalDocuments);
        Assert.Equal(34, r.TotalPackages);
        Assert.Equal(2, r.Days.Count);
        var d2 = r.Days.Single(d => d.Date == new DateOnly(2026, 9, 2));
        Assert.Equal(2, d2.Details.Count);
        Assert.Equal(2635125m, d2.Cost);
        Assert.All(d2.Details, x => Assert.Equal("Diurno", x.ShiftName));
        Assert.Contains(d2.Details, x => x.CarrierName == "TSM" && x.Cost == 2250125m);
        Assert.Contains(d2.Details, x => x.CarrierName == "MTO" && x.Cost == 385000m);
        Assert.Equal(0, r.ManifestsWithoutCost);
    }

    [Fact]
    public async Task Report_CuentaManifiestosSinCosto()
    {
        var (env, rep) = await BuildAsync();
        await MonthlyClosureServiceTests.SeedManifestAsync(env, 9, new DateOnly(2026, 9, 4), costFinal: null, costStatus: CostStatuses.NoRate);
        var r = await rep.GetMonthlyAsync(2026, 9);
        Assert.Equal(3232172m, r.TotalCost);
        Assert.Equal(1, r.ManifestsWithoutCost);
        Assert.Equal(3, r.Days.Count);
    }

    [Fact]
    public async Task ExportExcel_GeneraHojaConTotales()
    {
        var (_, rep) = await BuildAsync();
        var bytes = await rep.ExportMonthlyExcelAsync(2026, 9);

        using var wb = new XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Resumen");
        var total = ws.RowsUsed().First(r => r.Cell(1).GetString() == "Total");
        Assert.Equal(3232172m, total.Cell(4).GetValue<decimal>());
    }
}
