using Microsoft.Extensions.Options;
using Servicios.Common.Contratos;
using Servicios.Common.Logging;
using Xunit;

namespace Servicios.Common.Tests;

public class FileLogSinkTests : IDisposable
{
    private readonly string _carpeta;

    public FileLogSinkTests()
    {
        _carpeta = Path.Combine(Path.GetTempPath(), "servicios-sap-tests-" + Guid.NewGuid());
    }

    public void Dispose()
    {
        if (Directory.Exists(_carpeta))
        {
            Directory.Delete(_carpeta, recursive: true);
        }
    }

    private FileLogSink CrearSink()
    {
        var options = Options.Create(new LogSinkOptions { Folder = _carpeta });
        return new FileLogSink(options, prefijoServicio: "transferencia");
    }

    [Fact]
    public async Task WriteAsync_EscribeUnaLineaEnElArchivoDelDia()
    {
        var sink = CrearSink();
        var fecha = new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

        await sink.WriteAsync(new LogEntry
        {
            FechaHora = fecha,
            Nivel = NivelLog.Info,
            CompanyCode = "DEPOR",
            Mensaje = "ciclo iniciado"
        }, CancellationToken.None);

        var ruta = Path.Combine(_carpeta, "transferencia-2026-07-29.log");
        Assert.True(File.Exists(ruta));
        Assert.Contains("ciclo iniciado", await File.ReadAllTextAsync(ruta));
    }

    [Fact]
    public async Task PurgeOlderThanAsync_BorraSoloArchivosMasAntiguosQueLaRetencion()
    {
        var sink = CrearSink();
        var hoy = DateTimeOffset.UtcNow;

        await sink.WriteAsync(Entrada(hoy), CancellationToken.None);
        await sink.WriteAsync(Entrada(hoy.AddDays(-5)), CancellationToken.None);
        await sink.WriteAsync(Entrada(hoy.AddDays(-40)), CancellationToken.None);

        await sink.PurgeOlderThanAsync(retentionDays: 30, CancellationToken.None);

        var archivosRestantes = Directory.GetFiles(_carpeta).Select(Path.GetFileName).ToList();
        Assert.Contains($"transferencia-{hoy:yyyy-MM-dd}.log", archivosRestantes);
        Assert.Contains($"transferencia-{hoy.AddDays(-5):yyyy-MM-dd}.log", archivosRestantes);
        Assert.DoesNotContain($"transferencia-{hoy.AddDays(-40):yyyy-MM-dd}.log", archivosRestantes);
    }

    private static LogEntry Entrada(DateTimeOffset fecha) => new()
    {
        FechaHora = fecha,
        Nivel = NivelLog.Info,
        CompanyCode = "DEPOR",
        Mensaje = "evento de prueba"
    };
}
