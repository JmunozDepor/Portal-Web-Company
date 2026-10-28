# Modulo.FacturasProveedor — pendientes

Código de la Etapa 1 implementado en la rama `feat/facturas-proveedor` (plan
`docs/superpowers/plans/2026-10-27-facturas-proveedor-etapa1.md`, tareas 1 a 15). Suites al cerrar: Core 580,
Facturas de Proveedor 123, Despachos 699, Rendiciones 104 (todo en verde). Lo que sigue necesita bases reales,
credenciales o el Host, por eso queda para hacerlo con el usuario.

## Verificación FACELE en caoba (Task 2, Step 4) — PENDIENTE

Sin credenciales en variables de entorno al implementar (2026-10-27). Para correrla, definir FACELE_USER, FACELE_PASS,
FACELE_USER_MAIL y FACELE_RUT_RECEPTOR y ejecutar:

    dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter FullyQualifiedName~FaceleCaobaIntegrationTests

Confirmar y anotar:
- [ ] consultar (RECEPCION): estructura real de registros y que respuesta/estado = 1 significa OK.
- [ ] consultarReferencia: que trae la referencia 801 (OC) de un recibido con OC.
- [ ] actualizarObservacionesDTE sobre un recibido: si funciona, SupportsResponseNote = true e implementar SetNoteAsync.
- [ ] DocumentosRecibidosService?wsdl: si existe Obtener(XML|PDF), implementar GetXmlAsync/GetPdfAsync y SupportsDocumentDownload = true.
- [ ] aprobarDTERecibidos (solo con un documento de prueba): glosa real de "ya respondido" para ajustar AlreadyRespondedRegex.

## Puesta en marcha (Task 16) — PENDIENTE, con confirmación del usuario

- [ ] Merge de `feat/facturas-proveedor` a la rama de trabajo y `.\publish-dist.ps1` del módulo (detiene el Host local).
- [ ] Migración de plataforma `AddDteReception` en `portalsaas_saas_qa` y `ps_comdepor_qa`; verificar en `__EFMigrationsHistory`.
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
