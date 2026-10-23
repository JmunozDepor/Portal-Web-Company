using System.Text.RegularExpressions;
using Xunit;

namespace PortalSaas.Core.Tests.Infraestructura;

/// <summary>
/// REGLA DURA: en producción el portal corre bajo un PathBase (ej.
/// https://portal.grupodepor.cl/Depor/...). Una URL interna escrita como literal absoluto
/// ("/despachos/manifiestos") se salta ese prefijo y IIS responde "404 - File or directory
/// not found". En local no se nota porque no hay prefijo. Ya pasó en el login (2026-08-02),
/// el login de admin y Modulo.Despachos (2026-10-22, 12 enlaces).
///
/// Lo correcto: "~/..." en atributos de vistas (Razor lo resuelve con el PathBase),
/// asp-page/asp-route, Url.Content("~/...") o Url.Page(...) en C#/JS. Este test recorre
/// Core y todos los plugins y falla si encuentra un literal absoluto.
/// </summary>
public class UrlsRelativasAlPathBaseTests
{
    // href="/x", action="/x", src="/x", formaction="/x", data-*url="/x" -- "//cdn" (protocolo-relativo) no cuenta.
    private static readonly Regex AtributoAbsoluto = new(
        @"\b(href|action|src|formaction|data-[\w-]*url)\s*=\s*[""']/(?!/)[A-Za-z]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // fetch('/x'), location.href = '/x', location = '/x', window.open('/x').
    private static readonly Regex JsAbsoluto = new(
        @"(fetch\(|location(\.href)?\s*=|\.open\(|location\.(assign|replace)\()\s*[""'`]/(?!/)[A-Za-z]",
        RegexOptions.Compiled);

    // Redirect("/x"), LocalRedirect("/x"), RedirectPermanent($"/x").
    private static readonly Regex RedirectAbsoluto = new(
        @"\b(Redirect|LocalRedirect|RedirectPermanent)\(\s*\$?""/(?!/)[A-Za-z]",
        RegexOptions.Compiled);

    private static readonly string[] CarpetasExcluidas =
        { "bin", "obj", "dist", "node_modules", "lib", "artifacts", "TestResults" };

    [Fact]
    public void Vistas_y_codigo_no_usan_urls_internas_absolutas()
    {
        var raiz = BuscarRaizRepo();
        var carpetas = new[]
        {
            Path.Combine(raiz, "Portal SaaS - Core", "src"),
            Path.Combine(raiz, "Portal SaaS - Core", "plugins"),
            Path.Combine(raiz, "Portal SaaS - Plugins"),
        }.Where(Directory.Exists).ToArray();
        Assert.NotEmpty(carpetas);

        var hallazgos = new List<string>();
        foreach (var archivo in carpetas.SelectMany(c => ArchivosFuente(c)))
        {
            var ext = Path.GetExtension(archivo).ToLowerInvariant();
            if (ext == ".js" && archivo.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)) continue;
            var lineas = File.ReadAllLines(archivo);
            for (var i = 0; i < lineas.Length; i++)
            {
                var linea = lineas[i];
                var malo = ext switch
                {
                    ".cshtml" => AtributoAbsoluto.IsMatch(linea) || JsAbsoluto.IsMatch(linea),
                    ".js" => JsAbsoluto.IsMatch(linea),
                    ".cs" => RedirectAbsoluto.IsMatch(linea),
                    _ => false,
                };
                if (malo) hallazgos.Add($"{Path.GetRelativePath(raiz, archivo)}:{i + 1}: {linea.Trim()}");
            }
        }

        Assert.True(hallazgos.Count == 0,
            "URL interna absoluta (pierde el PathBase /Depor/ en producción -> 404 IIS). " +
            "Usar \"~/...\", asp-page o Url.Content(\"~/...\"):\n" + string.Join("\n", hallazgos));
    }

    private static IEnumerable<string> ArchivosFuente(string carpeta)
    {
        foreach (var archivo in Directory.EnumerateFiles(carpeta))
        {
            var ext = Path.GetExtension(archivo).ToLowerInvariant();
            if (ext is ".cshtml" or ".js" or ".cs") yield return archivo;
        }
        foreach (var sub in Directory.EnumerateDirectories(carpeta))
        {
            var nombre = Path.GetFileName(sub);
            if (CarpetasExcluidas.Contains(nombre, StringComparer.OrdinalIgnoreCase) || nombre.StartsWith('.')) continue;
            foreach (var archivo in ArchivosFuente(sub)) yield return archivo;
        }
    }

    private static string BuscarRaizRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Portal SaaS - Core")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repo desde " + AppContext.BaseDirectory);
    }
}
