# Módulo Despachos — Fase B: guías de traslado interno desde el manifiesto

**Fecha:** 2026-10-23 · **Estado:** diseño revisado con las respuestas del usuario (2026-10-23)
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
guía en los campos nativos `FolioNum` / `FolioPref` de la transferencia. El control de "guía emitida" lo lleva
el portal por `DocEntry` + objeto (67), igual que hoy lo lleva el add-on.

## 3. Decisiones

**Del usuario (2026-10-22 y 2026-10-23)**
1. **LPN:** campo de usuario `OWTR.U_NX_LPN`; **una transferencia = un LPN**.
2. **Escaneo de LPN opcional:** se puede agregar en bloque las pendientes de una sucursal.
3. **Transición:** no se sigue llenando `@INX_ODGC`; el add-on se retira cuando entre esta fase.
4. **Control de emitidas (2026-10-23):** hoy `OWTR.FolioNum` / `FolioPref` quedan vacíos; el add-on controla
   en su tabla de usuario por `DocEntry` + objeto. El portal lleva **el mismo control**: una transferencia está
   emitida si figura (objeto 67 + `DocEntry`) en una guía emitida del portal. Ese registro, no SAP, decide.
5. **Folio en el documento SAP:** además se graba el folio de la guía en los campos **nativos**
   `OWTR.FolioNum` (Service Layer `FolioNumber`) y `FolioPref` (`FolioPrefixString`). Es informativo: el POS
   no lo usa al recibir.
6. **Solo algunas bodegas son sucursales:** el resto son almacenes virtuales o de uso interno y **no** llevan
   guía. La compañía mantiene la lista de **bodegas sucursal**; una transferencia a cualquier otra bodega no
   entra al manifiesto.

**Del diseñador (revisar)**
7. **Ajustes por compañía** (Despachos > Orígenes):
   - prefijo de folio a grabar en `FolioPref` (default `52`); vacío = no se graba el folio en SAP;
   - campos de usuario opcionales "N° de manifiesto" y "fecha de despacho" (ej. `U_NX_Manifiesto`); vacío = no
     se escriben.
8. **Botón "Probar escritura":** reescribe el valor actual de esos campos en una transferencia cerrada de
   ejemplo (un PATCH sin cambio real). Valida, antes de operar, que SAP acepta actualizar `FolioNum` en un
   documento cerrado y que los campos de usuario existen. Si SAP no lo acepta, se desactiva la escritura y el
   control sigue funcionando igual, porque vive en el portal (§3.4).
9. **La guía no depende de la escritura en SAP.** La guía emitida es el documento legal. Si SAP rechaza la
   escritura, el despacho sigue y el documento queda con *"SAP pendiente"*, que se reintenta desde la pestaña
   Guías y se avisa por la campana. La escritura es idempotente: siempre se escriben los mismos valores.
10. **Guía de traslado interno:**
   - `IndTraslado` 5 (traslados internos) y `TipoDespacho` 3 (emisor a otras instalaciones), configurables
     aparte de los de facturas;
   - receptor = la propia compañía (RUT, razón social y giro del emisor, desde la configuración DTE);
   - destino = dirección de la bodega sucursal (`OWHS`, con dirección/comuna editables en la lista de
     bodegas sucursal por si en SAP están incompletas).

   Sin referencias: la transferencia SAP no es un DTE **[validar con el asesor]**.
11. **Agrupación:** una guía por bodega de destino (dirección + comuna), con las mismas reglas de división
    (60 líneas) que las facturas. Un manifiesto puede mezclar facturas y transferencias: salen guías separadas
    (las facturas con indicador 1 y referencias; las transferencias con indicador 5).
12. **El escáner reconoce solo qué leyó.** El timbre PDF417 es una factura; un texto que calza con el
    **patrón de LPN de la compañía** (expresión configurable, ej. `^(CTBO|ICBO)\d+$`) es una transferencia.
    Sin patrón configurado, el LPN se ingresa en un campo aparte.
13. **Plataforma:** contrato nuevo en `PortalSaas.Abstractions`,
    `ISapDocumentFieldWriter.UpdateHeaderFieldsAsync(objectType, docEntry, fields)`, implementado en Core con
    `SapSession.PatchAsync`. Así el plugin escribe en SAP sin referenciar Core, y otros módulos lo reutilizan.
14. **Sin tablas del add-on (usuario, 2026-10-23):** no se consultan `@INX_ODGC` / `@INX_DGC1`. El control de
    emitidas es solo del portal (§3.4). Las transferencias guiadas por el add-on antes de la puesta en marcha no
    se detectan: la fecha "desde" de las pendientes se fija a partir de la puesta en marcha.

## 4. Objetivos, no-objetivos, anti-objetivos

**Objetivos**
- Guía de traslado interno Res. 154 con detalle por artículo, emitida al despachar el manifiesto.
- Armar el manifiesto de reposición en segundos: "agregar todas las pendientes de la sucursal X".
- Que SAP muestre en la transferencia el folio de la guía con que salió.

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
SapTransferWriteback → ISapDocumentFieldWriter (Core, Service Layer PATCH StockTransfers:
   FolioNumber, FolioPrefixString y campos de usuario configurados)
   │ estado por documento: sap_status = ok | pending | error
```

### 5.1 Consultas SAP nuevas (configurables por compañía, con seed HANA, igual que las de la Fase A)

| Consulta | Parámetros | Devuelve |
|---|---|---|
| Transferencia por LPN | `:lpn` | Mismo contrato que la de facturas: `doc_entry`, `doc_num`, `document_date`, `customer_code` (= bodega destino), `customer_name` (= nombre de la bodega), `destination_address`, `destination_commune`, `units`, `packages` (= 1), `is_canceled`. Más `lpn` |
| Transferencias pendientes | `:warehouse`, `:from_date` | Una fila por transferencia no cancelada a esa bodega desde la fecha; mismas columnas. El portal descarta las que ya están en un manifiesto suyo |
| Bodegas | — | `OWHS`: código, nombre, dirección, comuna; para elegir las bodegas sucursal |
| Líneas de transferencia | `:doc_entry` | Mismo contrato que "Líneas de factura" (`WTR1`): artículo, nombre, cantidad, unidad, peso; ciudad de destino |

### 5.2 Datos nuevos

- `manifest_documents`:
  - `document_kind` admite `transfer`;
  - `folio` = DocNum de la transferencia (para mostrar);
  - nuevo `lpn`; índice por (compañía, objeto 67, `doc_entry`) para el control de emitidas;
  - nuevos `sap_writeback_status` y `sap_writeback_error`.
- `despachos_settings`:
  - `lpn_pattern`;
  - `transfer_folio_prefix`, `transfer_manifest_field`, `transfer_dispatch_date_field`;
  - `internal_transfer_indicator` (default 5), `internal_dispatch_type` (default 3).
- `despachos_branch_warehouses` (nueva): código de bodega, nombre, dirección y comuna (precargadas de `OWHS`,
  editables), activa.
- Platform: no tables; it only adds the `ISapDocumentFieldWriter` contract and its implementation.

### 5.3 Reglas

- **Agregar una transferencia:** se rechaza si
  - está cancelada;
  - va a una bodega que no es sucursal: "la bodega X no es sucursal";
  - ya está en otro manifiesto del portal no anulado;
  - está en otro manifiesto activo (misma regla que las facturas);
  - o su LPN no existe.
- **Agregar las pendientes de una sucursal:** suma todas las que pasan las reglas anteriores y muestra cuántas
  quedaron fuera y por qué.
- **Después de emitir:** por cada transferencia de la guía se escribe en SAP `FolioNum` + `FolioPref` y los
  campos de usuario configurados.
  - Si la escritura falla: `sap_writeback_status = pending` con el error, aviso por la campana y botón
    "Reintentar escritura SAP" en la pestaña Guías.
  - El manifiesto sí pasa a despachado.
- **Anular un manifiesto con guías pendientes:** igual que en la Fase A. No se escribe nada en SAP mientras la
  guía no esté emitida.

## 6. Preguntas abiertas

**Usuario** (respondidas 2026-10-23: control por `DocEntry` + objeto, folio en `FolioNum`/`FolioPref`, lista
de bodegas sucursal)
1. Prefijo de `FolioPref`: **`52`** (respondido 2026-10-23; queda configurable por compañía).

**Administrador SAP**
2. ¿Se quieren también manifiesto y fecha de despacho en `OWTR`? Si sí, nombres de los campos de usuario.
   El "Probar escritura" confirma si SAP deja actualizar `FolioNum` con el documento cerrado.

**Asesor tributario**
3. Guía de traslado interno: ¿receptor = la misma empresa con dirección de la sucursal, sin montos y sin
   referencias? ¿`TipoDespacho` 3?

## 7. Fases internas

1. **Contrato `ISapDocumentFieldWriter`** (Core) + ajustes de compañía, bodegas sucursal y "Probar escritura".
2. **Consultas SAP de transferencias** (4) + agregar por LPN y en bloque + reconocimiento de patrón en el escáner.
3. **Planificador:** grupo por bodega, IndTraslado 5, receptor propio.
4. **Escritura en SAP post-emisión,** estado por documento, reintento y aviso por campana.
5. **Prueba en QA** con DEPOR_TEST y caoba; luego, retiro del add-on.
