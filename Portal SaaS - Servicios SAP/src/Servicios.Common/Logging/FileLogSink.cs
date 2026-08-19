using System.Text.Json;
using Microsoft.Extensions.Options;
using Servicios.Common.Contratos;

namespace Servicios.Common.Logging;

/// <summary>
/// Un archivo por día (JSON-lines, UTF-8), bajo LogSinkOptions.Folder. Formato de nombre
/// fijo ("{prefijoServicio}-yyyy-MM-dd.log") para que PurgeOlderThanAsync pueda decidir
/// qué borrar leyendo solo el nombre del archivo, sin parsear contenido.
/// </summary>
public sealed class FileLogSink : ILogSink
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private readonly LogSinkOptions _options;
    private readonly string _prefijoServicio;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileLogSink(IOptions<LogSinkOptions> options, string prefijoServicio)
    {
        _options = options.Value;
        _prefijoServicio = prefijoServicio;

        // Un Windows Service arrancado por el SCM hereda C:\Windows\System32 como
        // directorio de trabajo del proceso, no la carpeta del .exe -- una ruta relativa
        // en Logging:Folder terminaría creándose ahí en vez de al lado del ejecutable.
        // Anclamos siempre a AppContext.BaseDirectory salvo que ya venga una ruta absoluta.
        _options.Folder = Path.IsPathRooted(_options.Folder)
            ? _options.Folder
            : Path.Combine(AppContext.BaseDirectory, _options.Folder);

        Directory.CreateDirectory(_options.Folder);
    }

    public async Task WriteAsync(LogEntry entry, CancellationToken ct)
    {
        var linea = JsonSerializer.Serialize(entry, SerializerOptions);
        var ruta = RutaArchivoDelDia(entry.FechaHora);

        await _lock.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(ruta, linea + Environment.NewLine, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task PurgeOlderThanAsync(int retentionDays, CancellationToken ct)
    {
        var corte = DateTimeOffset.UtcNow.Date.AddDays(-retentionDays);

        foreach (var ruta in Directory.EnumerateFiles(_options.Folder, $"{_prefijoServicio}-*.log"))
        {
            ct.ThrowIfCancellationRequested();

            if (TryParseFecha(ruta, out var fechaArchivo) && fechaArchivo < corte)
            {
                File.Delete(ruta);
            }
        }

        return Task.CompletedTask;
    }

    private string RutaArchivoDelDia(DateTimeOffset fechaHora)
        => Path.Combine(_options.Folder, $"{_prefijoServicio}-{fechaHora:yyyy-MM-dd}.log");

    private bool TryParseFecha(string ruta, out DateTime fecha)
    {
        fecha = default;
        var nombre = Path.GetFileNameWithoutExtension(ruta);
        var sufijo = nombre[(_prefijoServicio.Length + 1)..];
        return DateTime.TryParseExact(sufijo, "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.None, out fecha);
    }
}
