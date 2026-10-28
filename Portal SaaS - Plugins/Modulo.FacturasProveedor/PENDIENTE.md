# Modulo.FacturasProveedor — pendientes

Código de la Etapa 1 implementado en la rama `feat/facturas-proveedor` (plan
`docs/superpowers/plans/2026-10-27-facturas-proveedor-etapa1.md`, tareas 1 a 15). Suites al cerrar: Core 580,
Facturas de Proveedor 123, Despachos 699, Rendiciones 104 (todo en verde). Lo que sigue necesita bases reales,
credenciales o el Host, por eso queda para hacerlo con el usuario.

## Verificación FACELE en caoba (Task 2, Step 4) — HECHA EN PARTE (2026-10-28)

Con FACELE_USER, FACELE_PASS, FACELE_USER_MAIL y FACELE_RUT_RECEPTOR en variables de entorno (nunca en archivos):

    dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter FullyQualifiedName~FaceleCaobaIntegrationTests

Resultado (las dos pruebas en verde):
- [x] consultar (RECEPCION): estado = 1 → "Proceso OK" (confirmado). offset es por registro. 273 recibidos de
      Comercial Depor (abril-mayo 2026, copias del registro del SII, sin XML: "Envio Electronico no realizado").
      Los registros no traen estadoSII, url ni anulado. estadoLey20956: Por_Aceptar, Acepta_Contenido_Documento,
      No_Aplica (notas de crédito) y Documento_Pagado_Al_Contado (38 % del total: facturas al contado, que el SII no
      deja aceptar ni reclamar) → la plataforma las marca "no admite respuesta" (columna not_respondable_reason,
      migración AddDteReceivedNotRespondable) y el módulo las muestra en ese estado, sin evaluarlas.
- [x] Los dos servicios (`cl-dol` y `cl-dol-auth`) aceptan los headers; se usa la misma base que la emisión (`cl-dol-auth`).
- [x] Pasarela de emisión: el borrador de guía 52 devuelve PDF en caoba.
- [ ] consultarReferencia: en caoba ningún recibido trae referencias (no tienen XML). La 801 se verifica en la E2E de
      QA con facturas reales que traigan OC.
- [ ] actualizarObservacionesDTE / descarga de XML-PDF: sin verificar (caoba no tiene el XML de los recibidos).
- [ ] aprobarDTERecibidos: NO se llamó. Los recibidos de caoba son copias de facturas reales del SII y no hay un
      documento de prueba; responder uno podría llegar al registro real. Requiere un documento de prueba acordado
      con FACELE (y FACELE_ALLOW_RESPOND=1).

## E2E en bases QA (2026-10-28) — HECHA sin Host

Las bases QA de plataforma (`portalsaas_saas_qa`, `ps_comdepor_qa`) están vacías (solo esquema; Comercial Depor real
vive en `ps_comdepor` = PROD), así que la E2E se hizo con un arnés fuera del repo que usa las clases reales sobre
Postgres real: compañía de prueba `E2EFP` en `portalsaas_saas_qa` + módulo en `ps_comdepor_fp_qa`, FACELE caoba real,
SAP simulado (todos los emisores existen, sin OC), reloj fijado en 09-05-2026 11:00 (día siguiente a la última
recepción de caoba), "procesar desde" 20-04-2026, modo simulación. Resultado:
- [x] "Probar recepción" OK; sincronización inicial: 273 recibidos en ~2 min (una consultarReferencia por documento
      nuevo); segunda pasada: 0 nuevos / 0 cambios. Fechas en UTC, sin errores de largo de columnas.
- [x] Módulo: 259 revisiones (33/34/43); 104 "No admite respuesta" (contado); vencidas solo las de plazo ya pasado;
      sin OC → aviso y rechazo al vencer el aviso; gasto fijo (Verisure) → revisión y regla de último día. 0 respuestas
      al operador. Segunda evaluación sin cambios.
- [x] OC asociada a mano → reevaluación; decisión manual; segunda decisión con sello viejo rechazada.
- [x] Consultas de las pantallas (bandeja por grupo y texto, KPIs, detalle, mis aprobaciones) traducen bien a SQL.
      Encontró y se corrigió: la búsqueda por texto de la bandeja de plataforma distinguía mayúsculas en Postgres.
- Datos de prueba que quedaron: organización/compañía `E2EFP` (`portalsaas_saas_qa`, con su `company_dte_settings`
  y 273 `dte_received`) y sus revisiones en `ps_comdepor_fp_qa`. Se pueden borrar sin afectar nada.

## Puesta en marcha (Task 16) — PENDIENTE, con confirmación del usuario

- [x] Migraciones `AddDteReception` y `AddDteReceivedNotRespondable` aplicadas y verificadas en `portalsaas_saas_qa` y
      `ps_comdepor_qa` (2026-10-28). Falta PROD (`portalsaas_saas_prod`, `ps_comdepor`).
- [x] Base del módulo `ps_comdepor_fp_qa` creada (admin_saas, UTF8, Spanish_Chile) con `Inicial` aplicada. Falta `ps_comdepor_fp`.

- [ ] Merge de `feat/facturas-proveedor` a la rama de trabajo y `.\publish-dist.ps1` del módulo (detiene el Host local).
- [ ] Migraciones de plataforma `AddDteReception` y `AddDteReceivedNotRespondable` en `portalsaas_saas_qa` y `ps_comdepor_qa`; verificar en `__EFMigrationsHistory`.
      Después, **con confirmación**, en `portalsaas_saas_prod` y `ps_comdepor` (regla de migraciones en las 4 bases).
- [ ] Bases del módulo (nombres propuestos `ps_comdepor_fp_qa` y `ps_comdepor_fp`, en la instancia de módulos): crear y
      aplicar `Inicial` con `dotnet ef database update --project src/Modulo.FacturasProveedor.Migrations.Postgres`
      (variable `FACTURAS_PROVEEDOR_CONNECTION_STRING`).
- [ ] Admin: alta del módulo `FacturasProveedor`, contratarlo a la organización, conexión externa por compañía y perfiles
      con las acciones de la spec §13 (bandeja VIEW/EDIT, mis-aprobaciones VIEW, configuración VIEW/CREATE/EDIT/DELETE).
- [ ] /Admin › Compañía › Documentos electrónicos: sección "Recepción de documentos" (QA con caoba:
      `https://caoba.docele.cl/cl-dol`, correo del usuario FACELE, cada 30 minutos) y "Probar recepción".
- [ ] Junction `Portal SaaS - Core\artifacts\plugins\Modulo.FacturasProveedor` → `dist\Modulo.FacturasProveedor`;
      reiniciar el Host y revisar el log (sin "Colisión de ruta Razor" ni errores de carga).
- [ ] E2E en QA en modo **simulación**: probar las 4 consultas SAP con una OC y un RUT reales; crear el grupo de Finanzas,
      un proveedor de gasto fijo y "procesar desde" = hoy − 7; confirmar que la bandeja carga, que la evaluación coincide
      con lo esperado en 5 facturas reales y que nada se envía. Anotar el resultado aquí.
- [ ] Paso a modo Activo: lo decide Finanzas en la pantalla, después de cerrar con el asesor los puntos [validar] de la
      spec §16 (plazo exacto, ERM, tipo de reclamo).
