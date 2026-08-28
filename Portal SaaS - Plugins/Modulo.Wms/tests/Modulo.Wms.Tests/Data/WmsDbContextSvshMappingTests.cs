using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Xunit;

namespace Modulo.Wms.Tests.Data;

public class WmsDbContextSvshMappingTests
{
    [Fact]
    public void WmsOracleStageSvsh_SeMapeaConTablaYColumnasEsperadas()
    {
        var options = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var contexto = new WmsDbContext(options);

        var entityType = contexto.Model.FindEntityType(typeof(WmsOracleStageSvsh));

        Assert.NotNull(entityType);
        Assert.Equal("wms_oracle_stage_svsh", entityType!.GetTableName());
        Assert.Equal("line_id", entityType.FindProperty(nameof(WmsOracleStageSvsh.LineId))!.GetColumnName());
        Assert.Equal("shipment_nbr", entityType.FindProperty(nameof(WmsOracleStageSvsh.shipment_nbr))!.GetColumnName());
    }
}
