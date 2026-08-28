using System.Diagnostics;

namespace PortalSaas.Host.Infraestructura;

/// <summary>
/// Reinicio real del proceso del Host desde el backoffice -- pedido puntual para que
/// el operador de plataforma no tenga que pararse en el servidor a mano después de
/// importar un plugin nuevo (ver Pages/Admin/PlatformModules/Import.cshtml.cs).
/// PluginManager solo carga plugins en Build(), sin soporte de hot-reload -- esto es
/// lo mínimo real para que "importar" también signifique "quedar activo" sin
/// intervención manual del sistema operativo.
///
/// Sin un supervisor externo (IIS/servicio de Windows) que garantice que el proceso
/// vuelve a levantar solo, un simple Environment.Exit() dejaría el sitio caído para
/// siempre -- acá se lanza un watchdog de PowerShell desacoplado (Wait-Process por
/// PID + Start-Process con la misma ruta/argumentos/directorio de trabajo) ANTES de
/// pedir el shutdown gracioso, para no competir por el puerto TCP: el watchdog espera
/// a que este proceso termine de verdad (libera el puerto) antes de levantar el
/// próximo, no lo lanza en paralelo.
/// </summary>
public interface IApplicationRestartService
{
    /// <summary>Programa el reinicio: lanza el watchdog y detiene la app luego de <paramref name="delay"/>.</summary>
    void ScheduleRestart(TimeSpan delay);
}

public sealed class ApplicationRestartService : IApplicationRestartService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ApplicationRestartService> _logger;

    public ApplicationRestartService(IHostApplicationLifetime lifetime, ILogger<ApplicationRestartService> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public void ScheduleRestart(TimeSpan delay)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("No se pudo resolver Environment.ProcessPath -- no se puede armar el watchdog de reinicio.");

        // Environment.GetCommandLineArgs()[0] es la ruta del ejecutable/dll, no un
        // argumento real -- los argumentos reales (si los hay) empiezan en [1].
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        var workingDirectory = Environment.CurrentDirectory;
        var currentPid = Environment.ProcessId;

        var scriptPath = Path.Combine(Path.GetTempPath(), $"portalsaas-restart-{Guid.NewGuid():N}.ps1");
        var argList = string.Join(", ", args.Select(a => $"'{a.Replace("'", "''")}'"));
        var script = $"""
            Wait-Process -Id {currentPid} -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 1
            Start-Process -FilePath '{processPath.Replace("'", "''")}' -ArgumentList @({argList}) -WorkingDirectory '{workingDirectory.Replace("'", "''")}'
            Remove-Item -Path '{scriptPath.Replace("'", "''")}' -ErrorAction SilentlyContinue
            """;
        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        });

        _logger.LogWarning(
            "Reinicio del Host solicitado desde el backoffice -- watchdog lanzado para PID {Pid}, deteniendo la aplicación en {Delay}",
            currentPid, delay);

        _ = Task.Delay(delay).ContinueWith(_ => _lifetime.StopApplication());
    }
}
