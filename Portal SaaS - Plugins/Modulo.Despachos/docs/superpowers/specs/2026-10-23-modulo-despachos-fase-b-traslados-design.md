# Módulo Despachos — Fase B: guías de traslado interno desde el manifiesto

**Fecha:** 2026-10-23 · **Estado:** diseño para revisión
**Antecede:** `2026-10-22-modulo-despachos-etapa2-guias-res154-design.md` (§7.2) y la Fase A ya construida
(pasarela DTE de plataforma, planificador, emisión al despachar, pestaña Guías, guías pendientes).

## 1. Problema

Hoy las transferencias de stock a sucursales (`OWTR`) salen con la guía 52 que emite el add-on SAP
"Guía de Despacho" (`@INX_ODGC` / `@INX_DGC1`):
- una guía por sucursal, con el detalle **totalizado** ("103 CONVERSE — 9");
- **sin** los datos de transporte de la Res. 154.

Desde el 1-nov-2026 esa guía incumple. El manifiesto, que es el viaje real, debe emitirla.

## 2. Solución en una frase

El manifiesto acepta **transferencias** como unidad de carga (por LPN escaneado o en bloque por sucursal) y,
al despachar, emite por la misma pasarela de la Fase A **una guía de traslado interno por sucursal de destino**,
con una línea por artículo y los datos de transporte. Luego escribe en la transferencia de SAP el folio de la
guía y el N° de manifiesto, en campos de usuario configurables por compañía.

## 3. Decisiones

**Del usuario (2026-10-22 y 2026-10-23)**
1. **LPN:** campo de usuario `OWTR.U_NX_LPN`; **una transferencia = un LPN**.
2. **Escaneo de LPN opcional:** se puede agregar en bloque las pendientes de una sucursal.
3. **Transición:** no se sigue llenando `@INX_ODGC`; el add-on se retira cuando entre esta fase.
4. **Control de pendientes:** por el número de folio de la guía. **Interpretación del diseñador:** una
   transferencia está *pendiente de despacho* mientras su campo de usuario "folio de guía" (§3.6) esté vacío
   **[confirmar]**.
5. **Escritura en SAP:** los campos donde se guardan folio y manifiesto son **variables por compañía**.
   Falta validar si SAP permite actualizar campos de usuario de transferencias cerradas.

**Del diseñador (revisar)**
6. **Campos de usuario configurables** en Despachos > Ajustes, por compañía, todos opcionales:
   - "folio de guía" (ej. `U_NX_FolioGuia`): obligatorio para usar el control de pendientes;
   - "N° de manifiesto" (ej. `U_NX_Manifiesto`);
   - "fecha de despacho" (ej. `U_NX_FechaDesp`).

   Vacío = no se escribe ese dato.
7. **Botón "Probar escritura":** reescribe el valor actual de los campos en una transferencia cerrada de
   ejemplo (un PATCH sin cambio real). Así se valida, antes de operar, que SAP acepta la actualización y que los
   campos existen. Responde la duda del punto 5 sin depender del administrador SAP.
8. **La guía no depende de la escritura en SAP.** La guía emitida es el documento legal. Si SAP rechaza la
   escritura, el despacho sigue y el documento queda con *"SAP pendiente"*, que se reintenta desde la pestaña
   Guías y se avisa por la campana. La escritura es idempotente: siempre se escriben los mismos valores.
9. **Guía de traslado interno:**
   - `IndTraslado` 5 (traslados internos) y `TipoDespacho` 3 (emisor a otras instalaciones), configurables
     aparte de los de facturas;
   - receptor = la propia compañía (RUT, razón social y giro del emisor, desde la configuración DTE);
   - destino = dirección de la bodega de la sucursal (`OWHS`).

   Sin referencias: la transferencia SAP no es un DTE **[validar con el asesor]**.
10. **Agrupación:** una guía por bodega de destino (dirección + comuna), con las mismas reglas de división
    (60 líneas) que las facturas. Un manifiesto puede mezclar facturas y transferencias: salen guías separadas
    (las facturas con indicador 1 y referencias; las transferencias con indicador 5).
11. **El escáner reconoce solo qué leyó.** El timbre PDF417 es una factura; un texto que calza con el
    **patrón de LPN de la compañía** (expresión configurable, ej. `^(CTBO|ICBO)\d+$`) es una transferencia.
    Sin patrón configurado, el LPN se ingresa en un campo aparte.
12. **Plataforma:** contrato nuevo en `PortalSaas.Abstractions`,
    `ISapDocumentFieldWriter.UpdateHeaderFieldsAsync(objectType, docEntry, fields)`, implementado en Core con
    `SapSession.PatchAsync`. Así el plugin escribe en SAP sin referenciar Core, y otros módulos lo reutilizan.
13. **Coexistencia con el add-on:** las transferencias que ya tienen guía del add-on siguen entrando como hoy
    (tipo "guía de traslado" por folio, sin emitir nada). Solo las transferencias **sin** guía entran como
    "transferencia" y generan guía nueva. No hace falta una fecha de corte: la diferencia la da el folio.

## 4. Objetivos, no-objetivos, anti-objetivos

**Objetivos**
- Guía de traslado interno Res. 154 con detalle por artículo, emitida al despachar el manifiesto.
- Armar el manifiesto de reposición en segundos: "agregar todas las pendientes de la sucursal X".
- Que SAP sepa en qué guía y manifiesto salió cada transferencia.

**No-objetivos**
- Borrador de despacho / preparación previa (§7.3 del spec de Etapa 2): el modelo lo deja posible, pero no se construye.
- Recepción en tienda (la resuelve el POS).
- LPN obligatorio (queda como parámetro futuro).

**Anti-objetivos**
- Una transferencia en dos guías o en dos manifiestos activos.
- Que una falla al escribir en SAP deje al camión sin guía o anule una guía válida.
- Volver a totalizar el detalle.

## 5. Arquitectura

```
Escáner / "Agregar pendientes de sucursal"
   │ LPN → consulta SAP "transferencia por LPN"      pendientes → consulta "transferencias pendientes"
   ▼
manifest_documents (document_kind = "transfer")      ← mismo modelo que facturas
   │ Despachar
   ▼
GuidePlanner (+ grupo por bodega, IndTraslado 5)  →  pasarela DTE (Fase A, sin cambios)
   │ guía emitida
   ▼
SapTransferWriteback → ISapDocumentFieldWriter (Core, Service Layer PATCH StockTransfers)
   │ estado por documento: sap_status = ok | pending | error
```

### 5.1 Consultas SAP nuevas (configurables por compañía, con seed HANA, igual que las de la Fase A)

| Consulta | Parámetros | Devuelve |
|---|---|---|
| Transferencia por LPN | `:lpn` | Mismo contrato que la de facturas: `doc_entry`, `doc_num`, `document_date`, `customer_code` (= bodega destino), `customer_name` (= nombre de la bodega), `destination_address`, `destination_commune`, `units`, `packages` (= 1), `is_canceled`. Más `lpn` y `guide_folio` (el valor del campo de usuario "folio de guía", para rechazar las ya despachadas) |
| Transferencias pendientes | `:warehouse`, `:from_date` | Una fila por transferencia **sin folio de guía**, no cancelada, a esa bodega, desde la fecha; mismas columnas |
| Líneas de transferencia | `:doc_entry` | Mismo contrato que "Líneas de factura" (`WTR1`): artículo, nombre, cantidad, unidad, peso; ciudad de destino |

### 5.2 Datos nuevos

- `manifest_documents`:
  - `document_kind` admite `transfer`;
  - `folio` = DocNum de la transferencia (para mostrar);
  - nuevo `lpn`;
  - nuevos `sap_writeback_status` y `sap_writeback_error`.
- `despachos_settings`:
  - `lpn_pattern`;
  - `transfer_guide_folio_field`, `transfer_manifest_field`, `transfer_dispatch_date_field`;
  - `internal_transfer_indicator` (default 5), `internal_dispatch_type` (default 3).
- Platform: no tables; it only adds the `ISapDocumentFieldWriter` contract and its implementation.

### 5.3 Reglas

- **Agregar una transferencia:** se rechaza si
  - está cancelada;
  - ya tiene folio de guía en SAP: "ya despachada en la guía N";
  - está en otro manifiesto activo (misma regla que las facturas);
  - o su LPN no existe.
- **Agregar las pendientes de una sucursal:** suma todas las que pasan las reglas anteriores y muestra cuántas
  quedaron fuera y por qué.
- **Después de emitir:** por cada transferencia de la guía se escribe en SAP el folio, el manifiesto y la fecha
  (lo configurado).
  - Si la escritura falla: `sap_writeback_status = pending` con el error, aviso por la campana y botón
    "Reintentar escritura SAP" en la pestaña Guías.
  - El manifiesto sí pasa a despachado.
- **Anular un manifiesto con guías pendientes:** igual que en la Fase A. No se escribe nada en SAP mientras la
  guía no esté emitida.

## 6. Preguntas abiertas

**Usuario**
1. ¿Confirmas que "pendiente" = la transferencia no tiene folio de guía en su campo de usuario?
2. ¿Hay transferencias que **no** deben ir en guía (ej. entre bodegas del mismo CD)? Si las hay, ¿cómo se
   distinguen: serie, bodega de origen o tipo?

**Administrador SAP**
3. Nombres de los campos de usuario a crear en `OWTR` (folio de guía, manifiesto, fecha) en cada SAP
   (DEPOR y Block). El "Probar escritura" confirmará si se pueden actualizar con el documento cerrado.
4. Si SAP no permite actualizar transferencias cerradas: ¿hay otra tabla donde registrarlo, o basta con que
   quede solo en el portal?

**Asesor tributario**
5. Guía de traslado interno: ¿receptor = la misma empresa con dirección de la sucursal, sin montos y sin
   referencias? ¿`TipoDespacho` 3?

## 7. Fases internas

1. **Contrato `ISapDocumentFieldWriter`** (Core) + ajustes de compañía y "Probar escritura".
2. **Consultas SAP de transferencias** (3) + agregar por LPN y en bloque + reconocimiento de patrón en el escáner.
3. **Planificador:** grupo por bodega, IndTraslado 5, receptor propio.
4. **Escritura en SAP post-emisión,** estado por documento, reintento y aviso por campana.
5. **Prueba en QA** con DEPOR_TEST y caoba; luego, retiro del add-on.
