# Módulo Despachos — Guías manuales (DTE 52 sin documento SAP)

**Fecha:** 2026-10-24 · **Estado:** borrador para aprobación del usuario
**Antecede:** Fase A (pasarela DTE de plataforma, emisión al despachar, pestaña Guías) y Fase B (traslados).
Ejemplos reales del usuario: guías FACELE 197476 (cajas, traslado interno), 197477 (mueble, otros traslados
no venta) y 197478 (pallets de desecho, retira el receptor), todas del 23-10-2026.

## 1. Problema

Las guías de despacho que no nacen de una factura ni de una transferencia SAP (cajas, bolsas, insumos,
muebles, pallets de desecho) se digitan a mano en el formulario de FACELE (DocEle). Eso significa:
- se digitan datos que el portal ya tiene (receptor en SAP, dirección de sucursal, origen, transporte);
- quedan fuera del manifiesto, aunque viajan en el mismo camión;
- desde el 1-nov-2026 deben llevar los datos de transporte de la Res. 154, y en FACELE se olvidan con facilidad.

## 2. Solución en una frase

Una pantalla **Despachos → Guías manuales** para armar la guía como borrador:
- el receptor se busca en SAP o se digita; el detalle es libre; el IVA lo decide el motivo de traslado;
- se emite por la misma pasarela DTE de la Fase A, de dos maneras: dentro de un manifiesto (al despachar, con su
  transporte) o sola (botón **Emitir**, con transporte de los maestros o digitado).

Todas las guías del módulo, de manifiesto y manuales, quedan en una sola tabla índice.

## 3. Decisiones

**Del usuario (2026-10-24)**
1. **Viajan en manifiesto** cuando las lleva un transporte (ej. muebles). Se agregan al manifiesto como un
   documento más y salen en la hoja de ruta.
2. **Una sola tabla de guías emitidas** para todos los orígenes (manifiesto y manual), liviana. El detalle
   completo y el XML viven una sola vez en `dte_emissions` de la plataforma.
3. **IVA según el motivo de traslado.** El usuario no lo marca. Ejemplo: traslado interno sin IVA; otros
   traslados no venta con IVA 19%.
4. **Receptor desde los socios de negocio de SAP**, buscado por RUT. Si el RUT no existe en SAP, se digita
   **solo para esa guía** (no crea socio en SAP ni maestro en el portal).
5. **Datos de transporte Res. 154 obligatorios según corresponda.** Un transporte particular (persona sin
   relación con los maestros) se digita a mano en la guía.
6. **Momento de emisión:** al despachar el manifiesto, o con el botón **Emitir** si va sola. No se necesita el
   folio antes.

**Del diseñador (revisar)**
7. **Estados de la guía manual:**
   - Borrador (editable);
   - En manifiesto (agregada a un manifiesto en borrador; editable salvo transporte);
   - Pendiente / Emitida / Rechazada (mismos estados que la Fase A);
   - Anulada (solo un borrador; una guía emitida no se anula desde el portal).
8. **Código de borrador `GM-000123`** (correlativo por compañía), como `AT-` en Agrupar traslados. Con él la guía
   se agrega al manifiesto escaneando o digitando, igual que una hoja AT. El PDF borrador lo imprime en Code128.
9. **Motivos de traslado configurables** en Despachos → Configuración: lista de `IndTraslado` habilitados para
   guías manuales, cada uno con su marca "afecto a IVA".
   - Semilla: 5 traslados internos (sin IVA, activo), 6 otros traslados no venta (con IVA, activo).
   - Inactivos de inicio: 4 entrega gratuita, 7 devolución, 2 ventas por efectuar.
10. **Tipo de despacho** (SII `TipoDespacho`):
    - 1 = por cuenta del receptor;
    - 2 = del emisor a instalaciones del cliente;
    - 3 = del emisor a otras instalaciones.

    Lo elige el usuario y no tiene valor por defecto.
11. **Origen:** se elige de los Orígenes de Despachos (dirección y comuna). Un origen sin dirección o comuna no
    se puede usar.
12. **Formulario recortado** respecto del de FACELE. Quedan fuera: bultos y contenedores, campos forestales
    (Conaf, manzana/predio, latitud/longitud), descuentos y recargos globales, e indicador de servicio.
    Referencias: opcionales.

## 4. Objetivos, no-objetivos, anti-objetivos

**Objetivos**
- Dejar de digitar guías en FACELE para bolsas, insumos, muebles y desechos.
- Que toda guía de despacho emitida desde el portal cumpla la Res. 154.
- Una sola lista de guías del módulo, con filtros por origen, estado, fecha y receptor.

**No-objetivos**
- Crear socios de negocio en SAP o mover stock en SAP (estas guías no tocan inventario).
- Anular guías emitidas, emitir notas de crédito o débito.
- Otros DTE (facturas, boletas) manuales.

**Anti-objetivos** (aunque funcione, sería un fracaso)
- Una guía con despacho por cuenta del emisor timbrada sin patente ni chofer.
- Duplicar en Despachos el XML o las líneas que ya guarda `dte_emissions`.
- Una guía emitida dos veces por reintentos (la llave de emisión es estable, como en la Fase A).

## 5. Datos

### 5.1 `transport_guides` pasa a ser el índice de todas las guías

Columnas nuevas:

| Columna | Tipo | Uso |
|---|---|---|
| `source` | varchar(20), not null, default `manifest` | `manifest` (Fase A/B) o `manual` |
| `number` | int, null | correlativo `GM-` (solo manual); único por compañía |
| `dispatch_type` | int, null | SII `TipoDespacho` (1/2/3) |
| `origin_id` | bigint, null | Origen elegido (solo manual) |
| `receiver_card_code` | varchar(30), null | socio de negocio SAP; null = digitado |
| `transport_mode` | varchar(20), null | `manifest`, `master` o `manual` (solo manual) |
| `net_amount`, `vat_amount`, `total_amount` | numeric(18,2), null | totales calculados |
| `annul_reason` | varchar(250), null | motivo al anular un borrador |
| `created_by`, `issued_by` | uuid, null | auditoría |

Cambios en columnas existentes:
- `manifest_id` y `sequence` pasan a ser opcionales. Una guía manual no tiene manifiesto propio: viaja dentro de
  uno como documento (§5.2).
- El único `(manifest_id, sequence)` queda **filtrado** a `source = 'manifest'`. SQL Server trata los NULL
  como iguales en un índice único; Postgres no.
- `document_json` guarda el `DteDocument` del borrador o del plan. Se vacía al quedar emitida: desde ahí el
  contenido es el de `dte_emissions.request_json`.
- Los estados suman `draft`, `in_manifest` y `annulled` (solo manual).

**Por qué no solo el XML:**
- Listar y filtrar (comuna, estado, fecha) exige columnas indexables.
- Antes del folio no existe XML timbrado.
- Las columnas resumen pesan poco; el XML (unos 10–15 KB) queda una sola vez en la plataforma.

### 5.2 La guía manual dentro del manifiesto

- Nuevo tipo de documento del manifiesto: `DocumentKinds.ManualGuide = "manual_guide"`, con columna nueva
  `manifest_documents.transport_guide_id` (FK opcional).
- La fila toma del borrador:
  - el receptor (`CustomerName` y `CustomerTaxId`);
  - el destino (dirección y comuna, resuelta contra el catálogo);
  - las unidades (suma de cantidades), los bultos (1) y el monto total;
  - la fecha (la del borrador).

  Folio = 0 hasta que se emita; luego se escribe el folio real.
- Cuenta en totales, hoja de ruta, costo y retorno (entrega / no entregado) como cualquier documento.
- No es una guía que **ampara** documentos (`transport_guide_documents` no se usa). La guía manual **es** el
  documento.

### 5.3 Motivos de traslado

Tabla `transfer_motives`:
- `company_id`, `code` (int SII), `name`, `vat_affected` (bool), `is_active`;
- único `(company_id, code)`;
- se mantiene en Configuración.

## 6. Flujo

### 6.1 Crear (pantalla Guías manuales → Nueva)

**Receptor**
- Se digita el RUT, se valida el dígito verificador y se busca en SAP.
- Si existe:
  - trae razón social, giro y sus direcciones de despacho (`CRD1`, tipo S);
  - el usuario elige una dirección o la digita.
- Si no existe, se habilitan razón social, giro, dirección, comuna, ciudad, contacto y correo.
- **Atajo "a sucursal propia":** el receptor es la propia compañía (RUT, razón social y giro del emisor, desde
  la configuración DTE). El destino se elige de las bodegas sucursal (dirección y comuna ya cargadas).

**Condiciones**
- Fecha (hoy, editable a futuro, no al pasado).
- Motivo (lista §5.3) y tipo de despacho (§3.10).
- Forma de pago: contado por defecto.

**Origen:** se elige de los Orígenes (§3.11).

**Destino:** dirección, comuna y ciudad. Por defecto se copian del receptor y se pueden editar.

**Detalle**
- Hasta el máximo de líneas configurado para guías (`GuideMaxLines` de la Fase A).
- Cada línea lleva:
  - código (opcional), descripción (obligatoria) y unidad (opcional);
  - cantidad > 0 y precio unitario ≥ 0;
  - monto = cantidad × precio, redondeado a peso.

**Totales:** si el motivo es afecto, neto + IVA 19% = total; si no, total = neto.
**[validar con FACELE en el PDF borrador:** si el monto sin IVA va como neto con IVA 0 o como exento. El
ejemplo 197476 muestra total = neto.**]**

**Referencias:** opcionales (tipo, folio, fecha, razón).

**Transporte:** se elige un modo.

| Modo | Disponible | Datos |
|---|---|---|
| Del manifiesto | solo si la guía está en un manifiesto | el snapshot del manifiesto al despachar (patente, carro, chofer, transportista, fecha/hora salida) |
| De los maestros | guía sola | vehículo, chofer y transportista de Despachos |
| Particular | guía sola | se digitan patente, RUT y nombre del chofer, RUT y nombre del transportista (puede ser el mismo chofer) |

**Validación Res. 154** (la misma regla de la Fase A):
- con `TipoDespacho` 2 o 3: patente, RUT y nombre del chofer, y RUT del transportista son obligatorios;
- con 1: el transporte es opcional.

Los RUT se validan con su dígito verificador.

**Acciones:** Guardar borrador, Ver PDF borrador (vista previa del proveedor, sin folio) y Emitir (solo guía
sola).

### 6.2 Agregar al manifiesto

- En el manifiesto en borrador se escanea o digita `GM-000123` (mismo campo de escaneo que AT).
- También se puede usar el selector "Agregar guía manual", que lista los borradores de la compañía.
- La guía pasa a "En manifiesto" y su transporte a "Del manifiesto".
- Reglas:
  - una guía manual está en un solo manifiesto no anulado;
  - quitarla del manifiesto la devuelve a Borrador;
  - anular el manifiesto también la devuelve a Borrador.
- Mientras está en un manifiesto no se emite sola: el botón Emitir se reemplaza por "Se emite al despachar el
  manifiesto N° X".

### 6.3 Emitir

- **Al despachar:** el despacho emite las guías Res. 154 y las manuales del manifiesto en la misma corrida, con
  el mismo estado "guías pendientes", reintentos, PDF y campana.
  - El `DteDocument` de la manual se completa con el snapshot de transporte del manifiesto en ese momento.
  - Al emitirse, la fila del manifiesto recibe el folio.
- **Sola:** el botón Emitir valida, arma el `DteDocument` y lo envía a la pasarela con la llave de emisión
  estable de la guía.
  - Rechazada: se corrige y se reemite con la misma llave (el reemplazo de contenido sigue la regla de la
    Fase A).
  - En curso (proveedor caído): reintentar reenvía lo mismo.
- **Emitida:** solo lectura; se descarga el PDF.

### 6.4 Lista "Guías"

- Pantalla única con todas las guías de `transport_guides`.
- **Columnas:** origen (Manifiesto N° / Manual GM-), folio, fecha, receptor, comuna, motivo, total y estado.
- **Filtros:** origen, estado, rango de fechas y RUT o nombre del receptor.
- **Acciones:**
  - abrir: el manifiesto o el detalle de la guía manual;
  - PDF de la emitida;
  - Nueva guía manual.

## 7. SAP

Nueva consulta editable por compañía `business_partner_by_tax_id` (parámetro `:tax_id`) en Consultas SAP, con
seed en `Sql/Seeds/`:
- **Cabecera:** `OCRD` por `LicTradNum`, con `CardCode`, `CardName`, giro (campo configurable en la consulta)
  y `validFor`.
- **Direcciones:** `CRD1` tipo `S`, con dirección, comuna (`County` o `Block`) y ciudad.

La consulta acepta el RUT con o sin puntos y prueba con y sin guion. Recordar: los `:parámetros` en
comentarios se eliminan antes de ejecutar (fix e4472ef).

## 8. Permisos, menú y avisos

- **Menú:** `guias-manuales` bajo `grupo-operacion`, "Guías manuales", icono `bi-file-earmark-text`. La lista
  "Guías" (§6.4) es su página Index.
- **Acciones:**

| Acción | Permite |
|---|---|
| View | ver la lista, el detalle y los PDF |
| Create | crear y emitir |
| Edit | editar borradores |
| Delete | anular borradores |

- Agregar una guía manual a un manifiesto exige Edit del manifiesto, como cualquier documento.
- **Campana:** guía manual rechazada por el proveedor, con enlace al detalle (vía `TryEnqueueAsync`).
- **Reglas del repo:**
  - layout de página hermana: Detalle copia Agrupar traslados y el formulario copia Manifiestos/Cabecera;
  - URLs con `~/`;
  - responsive a 375 px;
  - `InvariantCulture` en los inputs numéricos;
  - migración aplicada en las bases del módulo QA y prod (`ps_comdepor_dsp_qa`, `ps_comdepor_dsp`).

## 9. Riesgos y temas abiertos

1. **Neto sin IVA vs exento** para motivos no afectos (§6.1): validar con el PDF borrador de FACELE antes de
   la primera emisión real.
2. **Giro y direcciones en SAP:** el campo de giro difiere entre Depor y Block. Por eso la consulta es
   editable.
3. **Fecha futura:** el SII acepta guías con fecha posterior. Si el manifiesto se despacha otro día, la guía
   toma la fecha de despacho. **[decisión del diseñador: la emisión al despachar usa la fecha del
   despacho]**.
4. **Producción emite guías reales:** las pruebas se hacen en DEPOR_TEST con FACELE en certificación.
