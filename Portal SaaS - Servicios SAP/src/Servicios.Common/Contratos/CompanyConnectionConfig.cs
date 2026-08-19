namespace Servicios.Common.Contratos;

/// <summary>
/// Motor de base de datos del SAP del cliente para esta compañía. Mismos nombres que
/// Instance.EngineType en Proyecto Saas Portal -- a propósito, para que una fila armada
/// hoy a mano en appsettings.json sea estructuralmente idéntica a la que el día de mañana
/// vendría de la tabla companies/instances de ese proyecto.
/// </summary>
public enum MotorBaseDatos
{
    Hana,
    SqlServer
}

/// <summary>
/// Todo lo que un servicio de esta familia necesita para conectarse al SAP de UNA
/// compañía: la base (HANA/SQL Server) y el Service Layer. Los campos *SecretCifrado*
/// vienen cifrados con ISecretoCifradoService (AES-256-GCM) -- nunca se guarda ni se pasa
/// una contraseña en texto plano fuera del momento de uso.
/// </summary>
public sealed class CompanyConnectionConfig
{
    /// <summary>Código corto de la compañía, usado solo para logging/diagnóstico (nunca como clave de negocio).</summary>
    public required string CompanyCode { get; init; }

    public required MotorBaseDatos EngineType { get; init; }

    public required string Host { get; init; }

    public required int Port { get; init; }

    /// <summary>
    /// Si la conexión a la base va cifrada (Encrypt=true;sslValidateCertificate=true en la
    /// cadena de conexión HANA). Default true -- pero configurable por compañía porque no
    /// toda instancia HANA de cliente tiene TLS habilitado en el puerto SQL (el server
    /// rechaza el handshake con "Invalid flags specified" si se pide cifrado y no lo
    /// soporta). Nunca se soluciona globalmente deshabilitando TLS -- se declara
    /// explícitamente qué compañías no lo soportan, ver CLAUDE.md.
    /// </summary>
    public bool DatabaseEncryptada { get; init; } = true;

    /// <summary>Current Schema en HANA / nombre de base en SQL Server.</summary>
    public required string Schema { get; init; }

    public required string DbUserId { get; init; }

    public required string DbSecretCifrado { get; init; }

    public required string ServiceLayerUrl { get; init; }

    public required string ServiceLayerUsername { get; init; }

    public required string ServiceLayerSecretCifrado { get; init; }

    /// <summary>
    /// Si true, el login a Service Layer tolera específicamente un certificado TLS cuyo
    /// nombre no coincide con el host configurado (SslPolicyErrors.
    /// RemoteCertificateNameMismatch) -- sigue validando que la cadena/firma del
    /// certificado sea válida, no es un bypass total. Default false: hay que declararlo
    /// explícitamente por compañía cuando su Service Layer usa un certificado interno con
    /// un nombre distinto al configurado en ServiceLayerUrl (ver CLAUDE.md, "TLS
    /// obligatorio siempre" -- esto es la excepción acotada y documentada, no la regla).
    /// </summary>
    public bool ToleraNombreCertificadoServiceLayer { get; init; }

    /// <summary>
    /// Si true, el login a Service Layer confía en el certificado TLS completo (nombre Y
    /// cadena/firma) sin validar nada -- para compañías de test con certificado
    /// autofirmado en Service Layer. El tráfico sigue yendo cifrado, solo se salta la
    /// validación. Más amplio que ToleraNombreCertificadoServiceLayer (que solo perdona el
    /// mismatch de nombre); usar este cuando además hay RemoteCertificateChainErrors.
    /// Excepción acotada y documentada por compañía, default false.
    /// </summary>
    public bool ConfiaCertificadoServiceLayer { get; init; }

    /// <summary>
    /// Cuántas bodegas de origen, en orden de prioridad, intenta esta compañía para
    /// cubrir la diferencia solicitada-comprometido. Reemplaza el "for i&lt;=3" hardcodeado
    /// del servicio legado -- configurable por compañía, no un límite global de código.
    /// </summary>
    public required int WarehousePriorityCount { get; init; }

    /// <summary>
    /// Nombre completo de la vista calculada de cabecera (ej. legado:
    /// "_SYS_BIC.""sap.clprddepor/NX_AUTO_ABS""") que trae DocEntry/ObjType/CardCode
    /// pendientes. En el servicio legado este nombre estaba fijo en Queries.cs con el
    /// paquete HANA de una sola compañía -- acá es dato de configuración por compañía,
    /// porque cada cliente despliega su propio paquete/vista en su propia instancia.
    /// </summary>
    public required string HeaderQuerySource { get; init; }

    /// <summary>
    /// Nombre del stored procedure/función que resuelve, por iteración de bodega, cuánta
    /// cantidad tomar de qué bodega (legado: "SP_DEP_ORDER_ABS"). El algoritmo interno del
    /// SP no cambia al portar (ver CLAUDE.md) -- lo que se parametriza es solo el NOMBRE,
    /// para que cada compañía pueda tener su propio SP si su ambiente lo requiere.
    /// </summary>
    public required string WarehouseAssignmentProcedure { get; init; }

    /// <summary>
    /// Nombre del campo de usuario (UDF) que se marca al terminar de recorrer las bodegas
    /// configuradas (legado: "U_NX_Auto_ABS", valor final 'N'). Configurable por compañía
    /// porque el UDF es un objeto propio de cada instalación SAP, no algo que el código
    /// pueda asumir fijo entre clientes.
    /// </summary>
    public required string CompletionUdfFieldName { get; init; }

    /// <summary>
    /// Nombre de la tabla normalizada (WhsCodeDestino, Prioridad, WhsCodeOrigen) que
    /// reemplaza al esquema de columnas fijas U_WhsCode1/2/3 -- usada por
    /// TransferenciaAutomatica_v2, no por v1 (que sigue con WarehouseAssignmentProcedure).
    /// Opcional porque v1 no la necesita.
    /// </summary>
    public string? WarehousePriorityTable { get; init; }

    /// <summary>
    /// Template de la reconsulta puntual de picking -- usada por TransferenciaAutomatica_v2
    /// antes de postear cada transferencia, para no asignar stock a un documento que ya
    /// entró a preparación. Contiene el placeholder "{TablaDetalle}" (reemplazado en código
    /// por RDR1/INV1/WTQ1 según ObjType) y el parámetro docEntry en la sintaxis del motor
    /// de esta compañía (ej. SQL Server: "SELECT COUNT(*) FROM {TablaDetalle} WHERE
    /// DocEntry = @docEntry AND PickIdNo IS NOT NULL"). Opcional porque v1 no la necesita.
    /// </summary>
    public string? PickingPendingQuery { get; init; }

    /// <summary>
    /// Si true, la conexión SQL Server confía en el certificado del servidor sin validar
    /// su cadena (TrustServerCertificate=true) -- el tráfico sigue yendo cifrado
    /// (Encrypt=true si DatabaseEncryptada lo pide), solo se salta la validación de la CA.
    /// Excepción acotada y documentada por compañía (típico en ambientes de test con
    /// certificado autofirmado), no un default -- mismo criterio que
    /// ToleraNombreCertificadoServiceLayer. No aplica a HANA (usa su propio
    /// sslValidateCertificate vía DatabaseEncryptada).
    /// </summary>
    public bool ConfiaCertificadoBaseDatos { get; init; }
}
