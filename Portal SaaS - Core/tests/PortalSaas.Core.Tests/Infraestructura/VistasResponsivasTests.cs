using System.Text.RegularExpressions;
using Xunit;

namespace PortalSaas.Core.Tests.Infraestructura;

/// <summary>
/// REGLA DURA (2026-10-22, ampliada 2026-10-23 al Host): toda vista del portal respeta el layout responsive para móviles. Revisión en
/// celular (375 px) encontró el mismo patrón en casi todos los módulos:
///   1. &lt;table&gt; suelta: en celular estira la tarjeta y la página entera se desplaza de
///      lado. Va dentro de un contenedor con scroll: "table-responsive-lg" (scroll solo
///      bajo 992 px, en escritorio conserva el encabezado sticky de .table-ps) o uno propio
///      de plataforma (*-wrapper con overflow-x). Excepción: tabla con menú dropdown en las
///      filas (el overflow lo recortaría) -- lleva su propio wrapper, ver rnd-table-wrapper.
///   2. class="btn-primary"/"btn-secondary" sin "btn": solo pone color; un &lt;a&gt; queda
///      subrayado y un &lt;button&gt; con el borde en relieve del navegador. Siempre
///      "btn btn-primary" (la forma la da .btn, ver CLAUDE.md "Densidad visual estándar").
/// Si este test falla, se corrige la vista, no el test.
/// </summary>
public class VistasResponsivasTests
{
    private static readonly Regex AperturaTabla = new(@"^\s*<table\b", RegexOptions.Compiled);
    private static readonly Regex ContenedorConScroll = new(@"table-responsive|-wrapper|overflow", RegexOptions.Compiled);
    private static readonly Regex AtributoClase = new(@"\bclass=""([^""]*)""", RegexOptions.Compiled);

    private static readonly string[] CarpetasExcluidas =
        { "bin", "obj", "dist", "node_modules", "lib", "artifacts", "TestResults" };

    [Fact]
    public void Tablas_de_plugins_van_dentro_de_un_contenedor_con_scroll()
    {
        var hallazgos = new List<string>();
        foreach (var (raiz, archivo) in VistasDePlugins())
        {
            var lineas = File.ReadAllLines(archivo);
            for (var i = 0; i < lineas.Length; i++)
            {
                if (!AperturaTabla.IsMatch(lineas[i])) continue;
                // El contenedor puede estar separado por un comentario Razor: se mira el
                // bloque de líneas no vacías inmediatamente anterior (hasta 6).
                var previas = lineas.Take(i).Reverse().Where(l => l.Trim().Length > 0).Take(6);
                if (previas.Any(l => ContenedorConScroll.IsMatch(l))) continue;
                var fin = Array.FindIndex(lineas, i, l => l.Trim() == "</table>");
                var bloque = string.Join("\n", lineas.Skip(i).Take((fin < 0 ? lineas.Length : fin + 1) - i));
                if (bloque.Contains("dropdown", StringComparison.Ordinal)) continue;
                hallazgos.Add($"{Path.GetRelativePath(raiz, archivo)}:{i + 1}");
            }
        }

        Assert.True(hallazgos.Count == 0,
            "Tabla sin contenedor con scroll (en celular desborda la página). Envolver en " +
            "<div class=\"table-responsive-lg\">:\n" + string.Join("\n", hallazgos));
    }

    [Fact]
    public void Botones_primary_y_secondary_llevan_la_clase_btn()
    {
        var hallazgos = new List<string>();
        foreach (var (raiz, archivo) in VistasDePlugins())
        {
            var lineas = File.ReadAllLines(archivo);
            for (var i = 0; i < lineas.Length; i++)
            {
                foreach (Match m in AtributoClase.Matches(lineas[i]))
                {
                    var tokens = m.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Any(t => t is "btn-primary" or "btn-secondary") && !tokens.Contains("btn"))
                        hallazgos.Add($"{Path.GetRelativePath(raiz, archivo)}:{i + 1}: {lineas[i].Trim()}");
                }
            }
        }

        Assert.True(hallazgos.Count == 0,
            "btn-primary/btn-secondary sin \"btn\" (enlace subrayado / botón sin forma). Usar " +
            "class=\"btn btn-primary\":\n" + string.Join("\n", hallazgos));
    }

    private static IEnumerable<(string Raiz, string Archivo)> VistasDePlugins()
    {
        var raiz = BuscarRaizRepo();
        var carpetas = new[]
        {
            Path.Combine(raiz, "Portal SaaS - Core", "src", "PortalSaas.Host", "Pages"),
            Path.Combine(raiz, "Portal SaaS - Core", "plugins"),
            Path.Combine(raiz, "Portal SaaS - Plugins"),
        }.Where(Directory.Exists).ToArray();
        Assert.NotEmpty(carpetas);
        return carpetas.SelectMany(c => Cshtml(c)).Select(a => (raiz, a));
    }

    private static IEnumerable<string> Cshtml(string carpeta)
    {
        foreach (var archivo in Directory.EnumerateFiles(carpeta, "*.cshtml")) yield return archivo;
        foreach (var sub in Directory.EnumerateDirectories(carpeta))
        {
            var nombre = Path.GetFileName(sub);
            if (CarpetasExcluidas.Contains(nombre, StringComparer.OrdinalIgnoreCase) || nombre.StartsWith('.')) continue;
            foreach (var archivo in Cshtml(sub)) yield return archivo;
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
