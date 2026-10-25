# Módulo Despachos — Entregas con guía previa

**Fecha:** 2026-10-25 · **Estado:** borrador para aprobación del usuario
**Antecede:** guías Res. 154 (Fase A/B), Agrupar traslados (AT-) y guías manuales (GM-).

## 1. Problema

Algunos clientes mayoristas (por ejemplo Falabella) reciben el despacho **solo con guía**. Solo cuando aprueban la
recepción se factura, al día siguiente y únicamente lo aceptado.

- Hoy esa guía se digita a mano en FACELE.
- Desde la Res. 154 la guía debe llevar el **detalle por producto** y los datos de transporte, y digitarla a mano
  ya no es viable.
- No se puede emitir la factura antes, porque el cliente puede rechazar total o parcialmente.

En SAP toda factura nace de **Entregas** (ODLN), y la Entrega es el documento que representa el despacho.

## 2. Solución en una frase

Una pantalla **Despachos → Entregas con guía previa** agrupa las Entregas pendientes de un cliente **por orden de
compra**. Las condiciones de la guía se eligen al agrupar: indicador de traslado (por defecto 2) y tipo de despacho.

La hoja de la agrupación (código **EG-000123**) se escanea en el manifiesto. Al despachar se emite **una guía 52
valorizada por OC**, con el detalle por producto, la referencia 801 a la OC y el transporte del manifiesto. El folio
de la guía se escribe en la Entrega de SAP.

## 3. Decisiones

**Del usuario (2026-10-24/25)**
1. **La guía nace de la Entrega**, no de una factura sin folio. Toda factura nace de Entregas en SAP.
2. **Una guía = una OC.** La OC está en el pedido (ORDR.`NumAtCard`). La Entrega lleva en `NumAtCard` el **LPN** que
   envía el WMS, así que la OC se obtiene por la relación Entrega → Pedido (DLN1.`BaseType` = 17, `BaseEntry`).
3. **Indicador de traslado 2 (ventas por efectuar)**: traslado que aún no constituye venta. Es un parámetro que se elige
   **al agrupar** (por defecto 2). No hay configuración por cliente.
4. **Abierto a cualquier cliente.** Lo que decide que una Entrega va con guía previa es que alguien la agrupe en esta
   pantalla. Las Entregas que no se agrupan siguen el camino de siempre (se despachan con su factura).
5. **Guía valorizada siempre**: precio y monto neto por línea, desde la Entrega.
6. **Referencia de la guía: 801** (orden de compra), como todos los documentos hoy.
7. **Rechazo parcial**: se factura solo lo aceptado (devolución sobre la Entrega en SAP). La guía no se toca.
8. **Plazo**: se factura al día siguiente de la recepción (proceso de SAP, fuera del portal).
9. **Referencia de la guía en la factura**: la resuelve el usuario con el proveedor del add-on de facturación. Queda
   fuera de este módulo (§8).

**Del diseñador (revisar)**
10. **Reutilizar Agrupar traslados.** La tabla de agrupaciones gana un tipo (`transfer` | `delivery`) y los datos
    propios de la entrega: cliente (código SAP y RUT), OC, indicador de traslado y tipo de despacho. Mismos estados
    (armada, en manifiesto, despachada, anulada), misma hoja PDF con Code128 y mismo escaneo en el manifiesto. Código
    visible **EG-** (las de traslados siguen siendo AT-).
11. **Tipo de despacho por defecto 2** (por cuenta del emisor a instalaciones del cliente). Se puede cambiar al
    agrupar.
12. **Una Entrega con líneas de más de una OC** (poco común) se muestra con advertencia y **no se puede agrupar**: hay
    que separarla en SAP. Así cada Entrega queda en una sola guía y la escritura del folio no es ambigua.
13. **Las Entregas solo entran al manifiesto por su agrupación** (hoja EG- o LPN de una Entrega agrupada). Una Entrega
    suelta no entra, porque ese camino sigue siendo el de la factura.

## 4. Objetivos, no-objetivos, anti-objetivos

**Objetivos**
- Emitir la guía de despacho con detalle por producto, valorizada y con transporte Res. 154, sin digitarla en FACELE.
- Una guía por OC, trazable desde la Entrega (folio escrito en SAP).

**No-objetivos**
- Emitir la factura desde el portal o agregarle la referencia a la guía (§8).
- Configuración por cliente.
- Registrar la aprobación o el rechazo del cliente: se hace en SAP con la devolución y la factura.

**Anti-objetivos**
- Una guía que mezcle dos OC.
- Una Entrega despachada dos veces, o en una guía y además con su factura en otro manifiesto.
- Una guía sin precios.

## 5. Flujo

### 5.1 Agrupar (Despachos → Entregas con guía previa → Agrupar)

1. **Filtros:** cliente (RUT o código SAP, buscado en SAP), fecha desde y fecha de despacho.
2. El portal trae las **Entregas abiertas** (no facturadas ni anuladas) del cliente desde la fecha, agrupadas **por
   OC**. Por cada OC muestra sus Entregas (DocNum, LPN, fecha, unidades y neto).
3. **Ocupadas:** una Entrega que ya está en una agrupación vigente o en un manifiesto no anulado no aparece (mismo
   control que las transferencias).
4. **Condiciones:** se eligen **una vez para lo que se agrupa**:
   - indicador de traslado: lista de los códigos SII, por defecto **2**;
   - tipo de despacho: por defecto **2**.
5. **Selección:** se marcan las OC o Entregas (nada marcado por defecto, con "seleccionar todo") y se presiona
   **Agrupar**. Resultado: una agrupación **por OC**, con código EG- y su hoja PDF (Code128, OC, cliente, LPN,
   unidades, sin montos).
6. **Detalle de la agrupación:** permite agregar una Entrega de la misma OC escaneando su LPN, quitar Entregas,
   cambiar las condiciones mientras está armada, anular e imprimir.

### 5.2 Manifiesto

- Se escanea la hoja **EG-**: entran sus Entregas como documentos del manifiesto (tipo **Entrega**), igual que las
  hojas AT-.
- **LPN de una Entrega:**
  - si está en una agrupación vigente, el manifiesto avisa que debe escanearse la hoja EG- (mismo comportamiento que
    con las transferencias agrupadas);
  - si no está agrupada, se rechaza: "La Entrega no está en una agrupación con guía previa; se despacha con su
    factura".
- **Datos de cada fila:** cliente, dirección y comuna de despacho de la Entrega, unidades, monto, fecha, LPN y código
  EG-.

### 5.3 Emisión al despachar

- **Una guía por agrupación (una OC).** Si excede el máximo de líneas, se divide en varias guías de la misma OC, como
  hoy.
- **Contenido de la guía:**
  - **Receptor:** el cliente (RUT, razón social y giro con la regla de las facturas: `U_U_NX_giro`,
    `U_GSP_SubGroupName`, `Notes`).
  - **Destino:** la dirección de despacho de la Entrega.
  - **Detalle:** por SKU o por modelo según el ajuste actual "Líneas de la guía"; **valorizado** con el neto de la
    línea de la Entrega (con el descuento de cabecera).
  - **Referencia:** 801 con la OC y la fecha del pedido.
  - **Indicador y tipo de despacho:** los de la agrupación.
  - **Transporte:** snapshot del manifiesto.
- **Folio en SAP:** después de emitir, el folio se escribe en la Entrega en los campos nativos `FolioPref` /
  `FolioNum` ("Número de folio" en SAP), con el mismo mecanismo y reintento ("SAP pendiente") de las transferencias.
- **Estados del manifiesto:** guías pendientes, reintentos, PDF y campana, igual que las guías Res. 154 de hoy.

## 6. SAP

Tres consultas nuevas, editables por compañía en Consultas SAP, con seed HANA:

| Consulta | Parámetros | Devuelve |
|---|---|---|
| `delivery_pending` | `:customer`, `:from_date` | Entregas abiertas del cliente: doc_entry, doc_num, fecha, lpn (`NumAtCard`), oc (ORDR.`NumAtCard` vía DLN1.`BaseEntry`), oc_count (cantidad de OC distintas), customer_code, customer_name, customer_tax_id, dirección y comuna de despacho, unidades y neto |
| `delivery_by_lpn` | `:lpn` | La misma fila para una Entrega por su LPN |
| `delivery_lines` | `:doc_entry` | Líneas DLN1 con el mismo contrato que `invoice_lines` (item, nombre, cantidad, unidad, peso, line_amount neto con descuento de cabecera, modelo, giro y ciudad de despacho) + oc y fecha del pedido |

- Escritura del folio: Service Layer `DeliveryNotes(DocEntry)` → `FolioNumber` y `FolioPrefixString`.
- Usa el mismo "Probar escritura" de Orígenes, ampliado a Entregas.

## 7. Permisos, menú y avisos

- **Menú:** `entregas-guia-previa` bajo Operación, con el nombre "Entregas con guía previa" y el icono
  `bi-box-arrow-right`. Mismas acciones que Agrupar traslados:

  | Acción | Permite |
  |---|---|
  | View | ver |
  | Create | agrupar |
  | Edit | agregar y quitar Entregas, cambiar condiciones |
  | Delete | anular |

- **Campana:** las mismas de las guías del manifiesto (guías pendientes, SAP pendiente).
- **Reglas del repo:** página hermana de Agrupar traslados, URLs con `~/`, responsive a 375 px, migración en
  `ps_comdepor_dsp_qa` y `ps_comdepor_dsp`.

## 8. Fuera de alcance y temas abiertos

1. **Referencia 52 en la factura:** el usuario lo ve con el proveedor del add-on de facturación. La propuesta para el
   proveedor: cuando la factura se copia de Entregas con "Número de folio", agregar la referencia 52 con ese folio,
   además de la 801. El portal deja el folio en la Entrega para que sea posible.
2. **Opcional, siguiente etapa:** reporte "Entregas despachadas pendientes de facturar" (OC, folio de guía, fecha).
3. **Validar con el asesor:** que el indicador 2 es el correcto para "despacho sujeto a aceptación del cliente".
4. **Entregas con más de una OC:** confirmar que son excepcionales (§3.12).
