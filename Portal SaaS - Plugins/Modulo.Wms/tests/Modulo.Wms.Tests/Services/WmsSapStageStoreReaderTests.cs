using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using Modulo.Wms.Services;
using Xunit;

namespace Modulo.Wms.Tests.Services;

public class WmsSapStageStoreReaderTests
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

        contexto.WmsSapStageStores.AddRange(
            new WmsSapStageStore { CompanyId = companyId, Pk = "C001", ExtraFieldsJson = """{"CardName":"A"}""", Status = WmsSapStageStatus.Pendiente },
            new WmsSapStageStore { CompanyId = companyId, Pk = "C002", ExtraFieldsJson = """{"CardName":"B"}""", Status = WmsSapStageStatus.ProcesadoWms },
            new WmsSapStageStore { CompanyId = otraCompanyId, Pk = "C003", ExtraFieldsJson = """{"CardName":"C"}""", Status = WmsSapStageStatus.Pendiente });
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageStoreReader(contexto);
        var resultado = await reader.LeerPendientesAsync(companyId, null, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("Store", registro["TipoDocumento"]);
        Assert.Equal("C001", registro["PK"]);
    }

    [Fact]
    public async Task LeerPendientesAsync_ConMasDe500PendientesDelMismoTipo_TopaEn500PorCiclo()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();

        for (var i = 0; i < 501; i++)
        {
            contexto.WmsSapStageStores.Add(new WmsSapStageStore
            {
                CompanyId = companyId,
                Pk = $"C{i:0000}",
                ExtraFieldsJson = """{"CardName":"N"}""",
                Status = WmsSapStageStatus.Pendiente,
                CreatedAt = DateTimeOffset.UtcNow.AddSeconds(i),
            });
        }
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageStoreReader(contexto);
        var resultado = await reader.LeerPendientesAsync(companyId, null, CancellationToken.None);

        Assert.Equal(500, resultado.Count);
    }

    [Fact]
    public async Task LeerPendientesAsync_ConExtraFields_IncluyeCadaClaveEnElRegistro()
    {
        var companyId = Guid.NewGuid();
        var contexto = CrearContexto();
        contexto.WmsSapStageStores.Add(new WmsSapStageStore
        {
            CompanyId = companyId,
            Pk = "C001",
            Status = WmsSapStageStatus.Pendiente,
            ExtraFieldsJson = """{"CardName":"Tienda","City":"Santiago"}""",
        });
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageStoreReader(contexto);
        var resultado = await reader.LeerPendientesAsync(companyId, null, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("C001", registro["PK"]);
        Assert.Equal("Tienda", registro.Fields["CardName"]?.ToString());
        Assert.Equal("Santiago", registro.Fields["City"]?.ToString());
    }

    [Fact]
    public async Task MarcarProcesadoAsync_Exito_ActualizaStatusYSyncedAtYReseteaValidacion()
    {
        var contexto = CrearContexto();
        var companyId = Guid.NewGuid();
        var fila = new WmsSapStageStore { CompanyId = companyId, Pk = "C001", ExtraFieldsJson = """{"CardName":"A"}""", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageStores.Add(fila);
        contexto.WmsExportValidations.Add(new WmsExportValidation
        {
            CompanyId = companyId,
            TipoDoc = "Store",
            Clave = "C001",
            Intentos = 15,
            WmsErrorMsg = "No confirmado en Oracle WMS Cloud tras 15 intentos.",
            ValidadoEn = DateTimeOffset.UtcNow.AddMinutes(-5),
            WmsStatusId = 101,
        });
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageStoreReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, null, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: true, mensajeError: null, CancellationToken.None);

        var actualizada = await contexto.WmsSapStageStores.SingleAsync();
        Assert.Equal(WmsSapStageStatus.Enviado, actualizada.Status);
        Assert.NotNull(actualizada.SyncedAt);

        var validacion = await contexto.WmsExportValidations
            .SingleAsync(v => v.CompanyId == companyId && v.TipoDoc == "Store" && v.Clave == "C001");
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
        var fila = new WmsSapStageStore { CompanyId = companyId, Pk = "C001", ExtraFieldsJson = """{"CardName":"A"}""", Status = WmsSapStageStatus.Pendiente };
        contexto.WmsSapStageStores.Add(fila);
        await contexto.SaveChangesAsync();

        var reader = new WmsSapStageStoreReader(contexto);
        var registro = (await reader.LeerPendientesAsync(companyId, null, CancellationToken.None)).Single();

        await reader.MarcarProcesadoAsync(companyId, registro, exito: false, mensajeError: "Rechazado por WMS", CancellationToken.None);

        var actualizada = await contexto.WmsSapStageStores.SingleAsync();
        Assert.Equal(WmsSapStageStatus.ErrorWms, actualizada.Status);
        Assert.Equal("Rechazado por WMS", actualizada.ErrorMsg);
    }
}
