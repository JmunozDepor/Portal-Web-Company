using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Lector del motor genérico de integración para las confirmaciones de
/// recepción SVSH -- agrupa por (shipment_nbr, BaseEntry) y arma un
/// IntegrationRecord por grupo. BaseType 1250000001 (StockTransfer) es el
/// único caso soportado hoy por SapDocumentConnector.PushAsync ("Inventory");
/// cualquier otro BaseType se marca "Purchase", que ese conector rechaza con
/// NotSupportedException explícito -- limitación conocida, no un bug de este
/// reader (ver spec 2026-08-20, sección 1).
/// </summary>
public class WmsSvshInventoryReader : IIntegrationEntityReader
{
    private const string BaseTypeStockTransfer = "1250000001";

    private readonly WmsDbContext _contexto;

    public WmsSvshInventoryReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "Wms.ConfirmacionIngreso";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var filas = await (
            from linea in _contexto.WmsOracleStageSvsh
            join stage in _contexto.WmsOracleInboundStages on linea.ParentId equals stage.Id
            where stage.CompanyId == companyId && linea.Status == WmsSvshStatus.Pendiente
            select linea
        ).ToListAsync(cancellationToken);

        var grupos = filas.GroupBy(f => (f.shipment_nbr, f.shipment_dtl_cust_field_2));
        var registros = new List<IntegrationRecord>();

        foreach (var grupo in grupos)
        {
            var lineas = new List<IntegrationRecord>();
            var idsDeLinea = new List<long>();

            foreach (var fila in grupo)
            {
                idsDeLinea.Add(fila.LineId);
                lineas.Add(new IntegrationRecord(new Dictionary<string, object?>
                {
                    ["ItemCode"] = fila.item_part_a,
                    ["Quantity"] = fila.received_qty,
                    ["BaseType"] = ParseIntOrNull(fila.shipment_dtl_cust_field_1),
                    ["BaseEntry"] = ParseIntOrNull(fila.shipment_dtl_cust_field_2),
                    ["BaseLine"] = ParseIntOrNull(fila.shipment_dtl_cust_field_3),
                }));
            }

            var primeraFila = grupo.First();
            var tipoDocumento = primeraFila.shipment_dtl_cust_field_1 == BaseTypeStockTransfer ? "Inventory" : "Purchase";

            registros.Add(new IntegrationRecord(new Dictionary<string, object?>
            {
                ["TipoDocumento"] = tipoDocumento,
                ["DocDate"] = ParseDateOrNull(primeraFila.rcvd_date),
                ["Lineas"] = lineas,
                ["_StagingLineIds"] = idsDeLinea,
            }));
        }

        return registros;
    }

    public async Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
    {
        var idsDeLinea = (List<long>)registro["_StagingLineIds"]!;

        var filas = await _contexto.WmsOracleStageSvsh
            .Where(f => idsDeLinea.Contains(f.LineId))
            .ToListAsync(cancellationToken);

        foreach (var fila in filas)
        {
            fila.Status = exito ? WmsSvshStatus.ProcesadoSap : WmsSvshStatus.ErrorSap;
            fila.ErrorMsg = exito ? null : mensajeError;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }

    private static int? ParseIntOrNull(string? valor) =>
        int.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var resultado) ? resultado : null;

    private static DateTime? ParseDateOrNull(string? valor) =>
        !string.IsNullOrWhiteSpace(valor) && DateTime.TryParse(valor, CultureInfo.InvariantCulture, DateTimeStyles.None, out var resultado) ? resultado : null;
}
