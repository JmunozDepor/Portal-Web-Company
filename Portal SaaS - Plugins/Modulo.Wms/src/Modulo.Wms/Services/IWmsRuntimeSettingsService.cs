using Modulo.Wms.Models;

namespace Modulo.Wms.Services;

/// <summary>
/// Metadato de una clave de <c>wms_runtime_settings</c>: etiqueta legible, ayuda,
/// valor por defecto de código y quién la consume. La lista es fija (whitelist) --
/// la página solo deja editar estas claves, nunca texto libre.
/// </summary>
public sealed record WmsRuntimeSettingKey(string Clave, string Etiqueta, string Ayuda, int Default, string UsadoPor);

public static class WmsRuntimeSettingsKeys
{
    public const string ExistsReconcilerMaxIntentos = "exists_reconciler.max_intentos";
    public const string SlshParserMaxIntentos = "slsh_parser.max_intentos";
    public const string SvshParserMaxIntentos = "svsh_parser.max_intentos";
    public const string StageErrorReconcilerStatusRechazado = "stage_error_reconciler.status_rechazado";

    /// <summary>Whitelist -- orden = orden de la página.</summary>
    public static readonly IReadOnlyList<WmsRuntimeSettingKey> Todas = new[]
    {
        new WmsRuntimeSettingKey(ExistsReconcilerMaxIntentos, "Reintentos de confirmación (Exists)",
            "Cuántas corridas espera WmsExistsReconciler la aparición de un registro en la entidad final de Oracle WMS Cloud antes de marcarlo Error WMS.",
            20, "WmsExistsReconciler"),
        new WmsRuntimeSettingKey(SlshParserMaxIntentos, "Reintentos de aplanado SLSH",
            "Cuántos fallos de persistencia tolera el parser de archivos SLSH (órdenes) antes de dejar la fila en ErrorStaging.",
            3, "WmsSlshStageParser"),
        new WmsRuntimeSettingKey(SvshParserMaxIntentos, "Reintentos de aplanado SVSH",
            "Cuántos fallos de persistencia tolera el parser de archivos SVSH (ingreso) antes de dejar la fila en ErrorStaging.",
            3, "WmsSvshStageParser"),
        new WmsRuntimeSettingKey(StageErrorReconcilerStatusRechazado, "Status de rechazo de Oracle",
            "status_id de LGFAPI que WmsStageErrorReconciler interpreta como rechazo terminal (Failed). No cambiar salvo que Oracle cambie su catálogo de estados.",
            101, "WmsStageErrorReconciler"),
    };
}

/// <summary>
/// Lee/escribe los ajustes de motor de <c>wms_runtime_settings</c>. Cachea 30 s para
/// que los loops de los background services no golpeen la BD cada iteración.
/// </summary>
public interface IWmsRuntimeSettingsService
{
    /// <summary>Valor entero de la clave, o <paramref name="codeDefault"/> si no hay fila / no parsea.</summary>
    Task<int> GetIntAsync(string clave, int codeDefault, CancellationToken ct = default);

    /// <summary>Todas las claves de la whitelist con su valor efectivo actual (fila o default).</summary>
    Task<IReadOnlyList<(WmsRuntimeSettingKey Meta, int ValorEfectivo, bool EsDefault)>> ListarAsync(CancellationToken ct = default);

    /// <summary>Fija (upsert) el valor de una clave de la whitelist. Debe ser entero.</summary>
    Task GuardarAsync(string clave, string valor, string updatedBy, CancellationToken ct = default);

    /// <summary>Borra la fila -> la clave vuelve a su default de código.</summary>
    Task RestaurarDefaultAsync(string clave, CancellationToken ct = default);
}
