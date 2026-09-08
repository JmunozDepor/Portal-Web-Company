namespace PortalSaas.Data.Entities.Integraciones;

public enum IntegrationConectorTipo { Sap, Rest, Archivo, WmsCloud, Sql }
public enum IntegrationDireccion { Subida, Bajada, Ambas }

public class IntegrationDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string ModuloOrigen { get; set; } = string.Empty;
    public string EntidadNegocio { get; set; } = string.Empty;
    public IntegrationConectorTipo ConectorTipo { get; set; }
    public string ConectorConfigCifrado { get; set; } = string.Empty;
    public IntegrationDireccion Direccion { get; set; }
    public bool Activo { get; set; } = true;

    /// <summary>Legado, ya no se usa -- reemplazado por <see cref="IntervaloMinutos"/>. La columna
    /// (cron_schedule) se conserva solo para no forzar una migración destructiva; ninguna
    /// pantalla la muestra ni ningún proceso la lee.</summary>
    public string? ProgramacionCron { get; set; }

    /// <summary>Cada cuántos minutos se reprograma la integración para correr sola. Null = solo
    /// corre cuando se la dispara a mano con "Ejecutar ahora" (fija <see cref="NextRunAt"/>).
    /// Con un valor, el hosted service de sincronización vuelve a fijar
    /// <see cref="NextRunAt"/> = ahora + IntervaloMinutos al terminar cada corrida. El mínimo
    /// efectivo real es 1 minuto: es cada cuánto el motor revisa qué hay pendiente.</summary>
    public int? IntervaloMinutos { get; set; }

    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>Marca de tiempo de la última corrida Bajada exitosa (Resultado=Exito), usada
    /// como cursor incremental: el conector (ver SapDocumentConnector) le agrega
    /// automáticamente "and UpdateDate ge &lt;este valor&gt;" al filtro configurado, así cada
    /// corrida solo trae de SAP lo que cambió desde la última vez, no el catálogo completo
    /// (antes: 29.083 Items completos en cada corrida, sin importar cuántos cambiaron -- 22 ago
    /// 2026). Null en la primera corrida -- trae todo lo que matchea el filtro, como antes.</summary>
    public DateTimeOffset? UltimaSincronizacionExitosa { get; set; }

    public ICollection<IntegrationFieldMapping> Mapeos { get; set; } = new List<IntegrationFieldMapping>();
}
