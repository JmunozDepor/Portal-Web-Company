using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsArchivoServiceTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    [Fact]
    public async Task ReintentarAsync_ArchivoEnError_VuelveAPendienteYLimpiaMensaje()
    {
        var companyId = Guid.NewGuid();
        var contexto = CrearContexto();

        var stage = new WmsOracleInboundStage
        {
            CompanyId = companyId, TipoDoc = "SLSH", Formato = WmsInboundFormato.Xml,
            NombreArchivo = "err.xml", HashArchivo = "hx", Contenido = "<Message></Message>",
            Estado = WmsInboundEstado.ErrorEstructura, MensajeError = "falló",
        };
        contexto.WmsOracleInboundStages.Add(stage);
        await contexto.SaveChangesAsync();

        var service = new WmsArchivoService(contexto);
        await service.ReintentarAsync(companyId, stage.Id, CancellationToken.None);

        var actualizado = await contexto.WmsOracleInboundStages.SingleAsync();
        Assert.Equal(WmsInboundEstado.Pendiente, actualizado.Estado);
        Assert.Null(actualizado.MensajeError);
    }

    [Fact]
    public async Task ListarAsync_FiltraPorTipoDocYEstado()
    {
        var companyId = Guid.NewGuid();
        var contexto = CrearContexto();

        contexto.WmsOracleInboundStages.AddRange(
            new WmsOracleInboundStage { CompanyId = companyId, TipoDoc = "SLSH", Formato = WmsInboundFormato.Xml, NombreArchivo = "a.xml", HashArchivo = "h1", Contenido = "", Estado = WmsInboundEstado.Pendiente },
            new WmsOracleInboundStage { CompanyId = companyId, TipoDoc = "SVSH", Formato = WmsInboundFormato.Xml, NombreArchivo = "b.xml", HashArchivo = "h2", Contenido = "", Estado = WmsInboundEstado.ErrorEstructura },
            new WmsOracleInboundStage { CompanyId = Guid.NewGuid(), TipoDoc = "SLSH", Formato = WmsInboundFormato.Xml, NombreArchivo = "c.xml", HashArchivo = "h3", Contenido = "", Estado = WmsInboundEstado.Pendiente });
        await contexto.SaveChangesAsync();

        var service = new WmsArchivoService(contexto);
        var resultado = await service.ListarAsync(companyId, "SLSH", null, 1, 25, CancellationToken.None);

        Assert.Single(resultado.Items);
        Assert.Equal("a.xml", resultado.Items[0].NombreArchivo);
        Assert.Equal(1, resultado.TotalCount);
    }
}
