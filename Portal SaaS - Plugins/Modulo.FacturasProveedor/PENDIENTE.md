# Modulo.FacturasProveedor — pendientes

## Verificación FACELE en caoba (Task 2, Step 4) — PENDIENTE

Sin credenciales en variables de entorno al implementar (2026-10-27). Para correrla, definir FACELE_USER, FACELE_PASS, FACELE_USER_MAIL y FACELE_RUT_RECEPTOR y ejecutar:

    dotnet test "Portal SaaS - Core/tests/PortalSaas.Core.Tests" --filter FullyQualifiedName~FaceleCaobaIntegrationTests

Confirmar y anotar:
- [ ] consultar (RECEPCION): estructura real de registros y que respuesta/estado = 1 significa OK.
- [ ] consultarReferencia: que trae la referencia 801 (OC) de un recibido con OC.
- [ ] actualizarObservacionesDTE sobre un recibido: si funciona, SupportsResponseNote = true e implementar SetNoteAsync.
- [ ] DocumentosRecibidosService?wsdl: si existe Obtener(XML|PDF), implementar GetXmlAsync/GetPdfAsync y SupportsDocumentDownload = true.
- [ ] aprobarDTERecibidos (solo con un documento de prueba): glosa real de "ya respondido" para ajustar AlreadyRespondedRegex.
