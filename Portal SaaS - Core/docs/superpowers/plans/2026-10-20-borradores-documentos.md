# Borradores de documentos — plan (2026-10-20)

Rama `feat/borradores`. Dos etapas, decididas con el dueño del proyecto.

## Decisiones

- **Etapa 1**: borrador para documentos **digitados** en los formularios genéricos
  (Venta 7 tipos, Compra 2, Inventario 2), incluido el detalle importado desde Excel.
- **Etapa 2**: Importación Genérica (masiva) puede **guardarse sin enviar**, sin edición
  de documentos — sirve para corregir precios/stock en SAP o esperar confirmación.
  "Revalidar" y "Enviar a SAP" los puede ejecutar **cualquiera con permiso**.
- **Visibilidad**: todos ven todos los borradores de la compañía; el filtro por defecto
  es "mis borradores". Cada borrador guarda **usuario creador** y un **motivo**
  obligatorio ("por qué está en borrador").
- **Vencimiento/limpieza**: parámetro **por compañía** (`company_document_settings`,
  días; vacío = no vencen), común a borradores digitados y masivos guardados.
- Persistencia en la base de la plataforma, por `CompanyId` (regla dura).

## Etapa 1 — piezas

1. **Datos**: `document_drafts` (motor, tipo, estado `draft|sending|sent|error`, motivo,
   creador, formulario completo en JSON, resumen para el listado, DocEntry/error de SAP,
   `version` como token de concurrencia) + `company_document_settings`.
   Migración aditiva `AddDocumentDrafts` (Postgres + SQL Server).
2. **Servicio** `IDocumentDraftService` (Core): guardar, obtener, listar (mis/todos,
   pendientes/enviados), eliminar, `TryBeginSendAsync` atómico (draft|error → sending;
   un "sending" de más de 10 min se puede reintentar), marcar enviado/error. Aviso por la
   campana al creador cuando otro usuario envía o elimina su borrador.
3. **Formulario** (clases base de los 3 motores + `DocumentForm`): panel de borrador
   dentro del form de creación — motivo, "Guardar borrador" (sin validar), "Validar"
   (restricciones + simulación SAP sin crear), "Eliminar". Abrir `?draft={id}` carga el
   borrador; "Crear" pasa a ser "Enviar a SAP": guarda lo editado, valida, crea y marca.
4. **Listados**: pestañas "Documentos SAP | Borradores" en los listados genéricos, con
   filtros "Creados por" (mis/todos) y "Estado".
5. **Limpieza**: `DocumentDraftCleanupWorker` aplica la retención por compañía.
   Parámetro editable en Administración → Formularios.
6. **Tests**: ciclo de vida, filtro por defecto, envío atómico/doble envío, limpieza.

## Etapa 2 — piezas

Estado nuevo en `GenericImportJob` ("Guardado"), visible en "Mis importaciones", excluido
de la limpieza de borradores temporales y sujeto a la retención por compañía; acciones
Revalidar / Enviar a SAP / Descartar; el control de archivo duplicado lo considera.
