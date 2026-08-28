using System.Reflection;
using System.Xml.Linq;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

public static class WmsSlshXmlParser
{
    private static readonly HashSet<string> ColumnasMetadata = new(StringComparer.OrdinalIgnoreCase)
    {
        "LineId", "ParentId", "Status", "ErrorMsg", "RetryCount", "SapDocEntry", "SapObject",
    };

    private static readonly PropertyInfo[] ColumnasDeNegocio = typeof(WmsOracleStageSlsh)
        .GetProperties()
        .Where(p => p.PropertyType == typeof(string) && !ColumnasMetadata.Contains(p.Name))
        .ToArray();

    public static IReadOnlyList<WmsOracleStageSlsh> Parse(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);

        var headerFields = doc.Descendants().Where(x => x.Name.LocalName == "Header")
            .Elements().ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);

        var loadFields = doc.Descendants().Where(x => x.Name.LocalName == "load")
            .Elements().ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);

        var obStops = doc.Descendants().Where(x => x.Name.LocalName == "ob_stop").ToList();

        var filas = new List<WmsOracleStageSlsh>();
        foreach (var obStop in obStops)
        {
            var stopFields = obStop.Elements()
                .ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);

            var fila = new WmsOracleStageSlsh();
            foreach (var columna in ColumnasDeNegocio)
            {
                string? valor = null;
                if (stopFields.TryGetValue(columna.Name, out var stopValue))
                {
                    valor = stopValue;
                }
                else if (loadFields.TryGetValue(columna.Name, out var loadValue))
                {
                    valor = loadValue;
                }
                else if (headerFields.TryGetValue(columna.Name, out var headerValue))
                {
                    valor = headerValue;
                }

                if (valor is not null)
                {
                    columna.SetValue(fila, valor);
                }
            }

            filas.Add(fila);
        }

        return filas;
    }
}
