using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Data;
using Modulo.Wms.Models;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace Modulo.Wms.Services;

/// <summary>
/// Lector del motor genérico de integración (IIntegrationEntityReader) para las
/// confirmaciones de traslado SLSH (Oracle WMS Cloud) aplanadas en Ronda A dentro de
/// wms_oracle_stage_slsh -- una fila por ob_stop del XML. Agrupa por
/// order_hdr_cust_field_4 (el BaseEntry real de la Solicitud de Traslado en SAP, un
/// mismo traslado puede tener varias líneas) y arma un IntegrationRecord por grupo
/// para que SapDocumentConnector lo postee como Copy-From (BaseType 1250000001).
/// </summary>
public class WmsSlshInventoryReader : IIntegrationEntityReader
{
    private readonly WmsDbContext _contexto;

    public WmsSlshInventoryReader(WmsDbContext contexto)
    {
        _contexto = contexto;
    }

    public string EntidadNegocio => "Wms.ConfirmacionTraslado";

    public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var filas = await (
            from linea in _contexto.WmsOracleStageSlsh
            join stage in _contexto.WmsOracleInboundStages on linea.ParentId equals stage.Id
            where stage.CompanyId == companyId && linea.Status == WmsSlshStatus.Pendiente
            select linea
        ).ToListAsync(cancellationToken);

        var grupos = filas.GroupBy(f => f.order_hdr_cust_field_4);
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
                    ["Quantity"] = fila.shipped_qty,
                    ["BaseType"] = 1250000001,
                    ["BaseEntry"] = ParseIntOrNull(fila.order_hdr_cust_field_4),
                    ["BaseLine"] = ParseIntOrNull(fila.order_dtl_cust_number_2),
                }));
            }

            registros.Add(new IntegrationRecord(new Dictionary<string, object?>
            {
                ["TipoDocumento"] = "Inventory",
                ["Lineas"] = lineas,
                ["_StagingLineIds"] = idsDeLinea,
            }));
        }

        return registros;
    }

    public async Task MarcarProcesadoAsync(Guid companyId, IntegrationRecord registro, bool exito, string? mensajeError, CancellationToken cancellationToken)
    {
        var idsDeLinea = (List<long>)registro["_StagingLineIds"]!;

        var filas = await _contexto.WmsOracleStageSlsh
            .Where(f => idsDeLinea.Contains(f.LineId))
            .ToListAsync(cancellationToken);

        foreach (var fila in filas)
        {
            fila.Status = exito ? WmsSlshStatus.ProcesadoSap : WmsSlshStatus.ErrorSap;
            fila.ErrorMsg = exito ? null : mensajeError;
        }

        await _contexto.SaveChangesAsync(cancellationToken);
    }

    private static int? ParseIntOrNull(string? valor) => int.TryParse(valor, out var resultado) ? resultado : null;
}
