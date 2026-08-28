using System.Reflection;
using System.Xml.Linq;
using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

/// <summary>
/// Aplanado del XML SVSH (confirmación de recepción WMS -> SAP), 3 niveles:
/// Header global -> ib_shipment -> ib_shipment_hdr + ib_shipment_dtl[]. Una
/// fila de salida por cada ib_shipment_dtl, resolviendo cada columna de
/// negocio con prioridad detalle > header del shipment > header global
/// (mismo criterio que SVSH_Processor.cs del legado). Espejo estructural de
/// WmsSlshXmlParser, adaptado a 3 niveles en vez de 1.
/// </summary>
public static class WmsSvshXmlParser
{
    private static readonly HashSet<string> ColumnasMetadata = new(StringComparer.OrdinalIgnoreCase)
    {
        "LineId", "ParentId", "Status", "ErrorMsg", "RetryCount", "SapDocEntry", "SapObject",
    };

    private static readonly PropertyInfo[] ColumnasDeNegocio = typeof(WmsOracleStageSvsh)
        .GetProperties()
        .Where(p => p.PropertyType == typeof(string) && !ColumnasMetadata.Contains(p.Name))
        .ToArray();

    public static IReadOnlyList<WmsOracleStageSvsh> Parse(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);

        var headerFields = doc.Descendants().Where(x => x.Name.LocalName == "Header")
            .Elements().ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);

        var filas = new List<WmsOracleStageSvsh>();

        foreach (var shipment in doc.Descendants().Where(x => x.Name.LocalName == "ib_shipment"))
        {
            var shipmentHdrFields = shipment.Elements()
                .FirstOrDefault(x => x.Name.LocalName == "ib_shipment_hdr")?
                .Elements().ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var detalle in shipment.Elements().Where(x => x.Name.LocalName == "ib_shipment_dtl"))
            {
                var detalleFields = detalle.Elements()
                    .ToDictionary(e => e.Name.LocalName, e => e.Value, StringComparer.OrdinalIgnoreCase);

                var fila = new WmsOracleStageSvsh();
                foreach (var columna in ColumnasDeNegocio)
                {
                    string? valor = null;
                    if (detalleFields.TryGetValue(columna.Name, out var detalleValor))
                    {
                        valor = detalleValor;
                    }
                    else if (shipmentHdrFields.TryGetValue(columna.Name, out var hdrValor))
                    {
                        valor = hdrValor;
                    }
                    else if (headerFields.TryGetValue(columna.Name, out var globalValor))
                    {
                        valor = globalValor;
                    }

                    if (valor is not null)
                    {
                        columna.SetValue(fila, valor);
                    }
                }

                filas.Add(fila);
            }
        }

        return filas;
    }
}
