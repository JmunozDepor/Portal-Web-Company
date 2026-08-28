using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageItemReaderTests
{
    private static WmsDbContext CrearContexto()
    {
        var opciones = new DbContextOptionsBuilder<WmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new WmsDbContext(opciones);
    }

    [Fact]
    public async Task LeerPendientesAsync_SoloTraePendientesDeLaCompania()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var otraCompanyId = Guid.NewGuid();

        contexto.WmsSapStageItems.AddRange(
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Pendiente },
            new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM002", ItemName = "B", Status = WmsSapStageStatus.ProcesadoWms },
            new WmsSapStageItem { CompanyId = otraCompanyId, ItemCode = "ITM003", ItemName = "C", Status = WmsSapStageStatus.Pendiente });
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageItemReader(contexto);
        var resultado = await reader.LeerPendientesAsync(companyId, null, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("Item", registro["TipoDocumento"]);
        Assert.Equal("ITM001", registro["item_alternate_code"]);
    }

    [Fact]
    public async Task LeerPendientesAsync_ConMasDe500PendientesDelMismoTipo_TopaEn500PorCiclo()
    {
        // Regresión confirmada 22 ago 2026 contra Oracle WMS Cloud real: sin este tope, un
        // backlog grande (29.387 items tras un backfill completo) hacía que PushAsync nunca
        // retornara dentro del timeout de 300s, y ningún envío exitoso quedaba registrado
        // aunque Oracle ya los hubiera aceptado. Ver doc-comment de MaximoPorCiclo.
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();

        for (var i = 0; i < 501; i++)
        {
            contexto.WmsSapStageItems.Add(new WmsSapStageItem
            {
                CompanyId = companyId,
                ItemCode = $"ITM{i:0000}",
                ItemName = "N",
                Status = WmsSapStageStatus.Pendiente,
                CreatedAt = DateTimeOffset.UtcNow.AddSeconds(i),
            });
        }
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageItemReader(contexto);
        var resultado = await reader.LeerPendientesAsync(companyId, null, CancellationToken.None);

        Assert.Equal(500, resultado.Count);
    }

    [Fact]
    public async Task LeerPendientesAsync_ConExtraFields_IncluyeCadaClaveEnElRegistro()
    {
        var companyId = Guid.NewGuid();
        var contexto = CrearContexto();
        contexto.WmsSapStageItems.Add(new WmsSapStageItem
        {
            CompanyId = companyId,
            ItemCode = "ITM1",
            ItemName = "Nombre",
            BarCode = "123",
            Status = WmsSapStageStatus.Pendiente,
            ExtraFieldsJson = """{"brand_code":"NIKE","putaway_type":"A"}""",
        });
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageItemReader(contexto);
        var resultado = await reader.LeerPendientesAsync(companyId, null, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("NIKE", registro.Fields["brand_code"]?.ToString());
        Assert.Equal("A", registro.Fields["putaway_type"]?.ToString());
        Assert.Equal("ITM1", registro["item_alternate_code"]);
        Assert.Equal("Nombre", registro["description"]);
        Assert.Equal("123", registro["barcode"]);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Exito_ActualizaStatusYSyncedAt()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var fila = new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageItems.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageItemReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, null, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: true, mensajeError: null, CancellationToken.None);

        var actualizada = await contexto.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.Enviado, actualizada.Status);
        Assert.NotNull(actualizada.SyncedAt);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_ExitoConValidacionPrevia_ReseteaIntentosYLimpiaError()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var fila = new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageItems.Add(fila);
        // Simula un ciclo previo (antes del reenvío) donde el ítem acumuló casi el máximo de
        // intentos y quedó con error -- el reenvío debe resetear esta fila, no heredarla.
        contexto.WmsExportValidations.Add(new WmsExportValidation
        {
            CompanyId = companyId,
            TipoDoc = "Item",
            Clave = "ITM001",
            Intentos = 15,
            WmsErrorMsg = "No confirmado en Oracle WMS Cloud tras 15 intentos.",
            ValidadoEn = DateTimeOffset.UtcNow.AddMinutes(-5),
            WmsStatusId = 101,
        });
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageItemReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, null, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: true, mensajeError: null, CancellationToken.None);

        var validacion = await contexto.WmsExportValidations
            .SingleAsync(v => v.CompanyId == companyId && v.TipoDoc == "Item" && v.Clave == "ITM001");
        Assert.Equal(0, validacion.Intentos);
        Assert.Null(validacion.WmsErrorMsg);
        Assert.Null(validacion.ValidadoEn);
        Assert.Null(validacion.WmsStatusId);
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Error_ActualizaStatusYErrorMsg()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var fila = new WmsSapStageItem { CompanyId = companyId, ItemCode = "ITM001", ItemName = "A", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageItems.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageItemReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, null, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: false, mensajeError: "Rechazado por WMS", CancellationToken.None);

        var actualizada = await contexto.WmsSapStageItems.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ErrorWms, actualizada.Status);
        Assert.Equal("Rechazado por WMS", actualizada.ErrorMsg);
    }
}
