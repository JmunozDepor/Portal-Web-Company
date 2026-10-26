using PortalSaas.Core.Infraestructura;

namespace PortalSaas.Core.Tests.Infraestructura;

/// <summary>
/// Librerías nativas de un plugin publicado sin .deps.json (publish-all.ps1 lo filtra): bug real en producción
/// (2026-10-26), "The type initializer for 'SkiaSharp.SKImageInfo' threw an exception" al imprimir la hoja de
/// Agrupar traslados porque libSkiaSharp.dll vive en runtimes/win-x64/native y nadie la buscaba ahí.
/// </summary>
public class PluginLoadContextNativeTests
{
    private static string TempPluginDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "plc-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void SinDepsJson_EncuentraLaNativaEnRuntimesDeLaPlataforma()
    {
        var dir = TempPluginDir();
        var native = Path.Combine(dir, "runtimes", PluginLoadContext.CurrentRuntimeId(), "native");
        Directory.CreateDirectory(native);
        var file = Path.Combine(native, PluginLoadContext.NativeFileNames("libSkiaSharp")[0]);
        File.WriteAllBytes(file, Array.Empty<byte>());

        Assert.Equal(file, PluginLoadContext.FindNativeInRuntimes(dir, "libSkiaSharp"));
    }

    [Fact]
    public void SinCarpetaRuntimes_DevuelveNull()
    {
        Assert.Null(PluginLoadContext.FindNativeInRuntimes(TempPluginDir(), "libSkiaSharp"));
    }

    [Fact]
    public void RuntimeId_EsSistemaYArquitectura()
    {
        Assert.Matches(@"^(win|linux|osx)-(x64|x86|arm64|arm)$", PluginLoadContext.CurrentRuntimeId());
    }
}
