# Módulo Despachos — Pre-guías (agrupación de transferencias por sucursal)

Fecha: 2026-10-23 · Estado: borrador para aprobación del usuario · Antecedentes: Fase B
(`2026-10-23-modulo-despachos-fase-b-traslados-design.md`).

## 1. Intención

Hoy el add-on "Guía de Despacho" (SAP) agrupa a mano las transferencias de una tienda en una guía. En el portal, la guía
de traslado se emite al despachar el manifiesto. Falta el paso previo: **quien prepara** (bodega) deja agrupados, por
sucursal y para un día de despacho, los LPN que están listos. Después **despacho** solo escanea una hoja por grupo en el
manifiesto y entran todas sus transferencias, sin escanear LPN por LPN.

Una tienda puede tener **más de una pre-guía el mismo día**. Es un corte por horario: lo que estaba listo al generar
queda en el grupo 1, y lo que se transfiere después queda en el grupo 2 al volver a generar.

Lo único relevante es la **cantidad** (unidades, LPN). No se muestran montos.

## 2. Objetivos y no-objetivos

Objetivos:
- Generar pre-guías por sucursal desde las transferencias SAP pendientes, con clics y sin escanear.
- Hoja impresa mínima por pre-guía, que despacho escanea en el manifiesto.
- Que un LPN no quede en dos pre-guías ni se despache por fuera de su pre-guía.

No-objetivos:
- La pre-guía **no emite** ningún documento tributario. La guía 52 se sigue emitiendo al despachar el manifiesto, con
  los datos de transporte (Res. 154). La agrupación de la guía la sigue haciendo GuidePlanner por destino.
- Facturas anticipadas en pre-guías: no se incluyen; solo transferencias (OWTR con LPN).
- Guías manuales (bolsas, insumos): van en otra especificación.
- Lectura por cámara multiformato: va aparte. El lector de mano lee Code 128 sin cambios.
- Montos o valorización.

## 3. Comportamiento

### 3.1 Generar pre-guías (menú nuevo "Pre-guías")
1. Filtros:
   - **fecha de despacho**, por defecto hoy;
   - **transferencias desde**, por defecto hoy, igual que el "Desde" de "Agregar pendientes", que debe ser ≥ la
     puesta en marcha;
   - **sucursal**: todas, o una.
2. "Buscar" trae de SAP las transferencias no anuladas hacia **bodegas sucursal activas** y descarta las que ya están
   en una pre-guía activa o en un manifiesto activo. El resultado se ve agrupado por sucursal, con la cantidad de LPN y
   las unidades. Cada transferencia trae un check marcado por defecto.
3. "Generar pre-guías" crea **una pre-guía por sucursal** con las transferencias marcadas. Cada pre-guía guarda una
   foto de los datos (DocEntry, DocNum, LPN, unidades, fecha), así que imprimir no vuelve a consultar SAP.
4. Volver a generar más tarde solo muestra lo nuevo, y eso produce el grupo 2, 3… de la sucursal para ese día.

Consulta SAP: una consulta editable nueva, **"Transferencias pendientes (todas las sucursales)"**, con `:from_date`.
Tiene las mismas columnas que "Transferencias pendientes" y no lleva parámetro de bodega: el portal filtra por su
lista de bodegas sucursal. Así se evita una consulta por tienda. Se siembra igual que las otras.

### 3.2 La pre-guía
- Número correlativo por compañía. Se muestra como **PG-000123**, y ese mismo texto va en el código de barras.
- Datos: sucursal (código y nombre), fecha de despacho, n° de grupo del día para esa sucursal, quién la creó y cuándo.
- Estados:

  | Estado | Cuándo |
  |---|---|
  | Armada | Recién creada, o al salir de un manifiesto |
  | En manifiesto | Está en un manifiesto borrador |
  | Despachada | Su manifiesto se despachó |
  | Anulada | Se anuló estando armada; sus LPN quedan libres |

- Mientras está **Armada** se puede:
  - quitar una transferencia;
  - agregar un LPN escaneándolo, con las mismas reglas de la Fase B (existe, no anulado, va a esa misma sucursal,
    no está en otra pre-guía ni en otro manifiesto).
- Listado de pre-guías filtrable por fecha de despacho, sucursal y estado.

### 3.3 Hoja impresa
Una página por pre-guía. Se imprime una sola o todas las del día filtrado en un único PDF (MigraDoc +
`BarcodeRenderer.Code128Png`, como el PDF del manifiesto). Contenido:
- código de barras Code 128 grande con `PG-000123` y el número en texto;
- sucursal, fecha de despacho y "Grupo n";
- tabla LPN | Transferencia (DocNum) | Unidades;
- totales: cantidad de LPN y unidades.

### 3.4 En el manifiesto
- En el mismo campo de escaneo, un texto con forma `PG-` + dígitos se reconoce como pre-guía antes de intentar leerlo
  como LPN. Al escanearlo:
  - se agregan todas sus transferencias con las reglas de `AddTransferAsync`, revalidando cada una contra SAP por si
    se anuló después de armar el grupo;
  - las que se rechacen se informan una por una y el resto entra igual;
  - cada `ManifestDocument` queda ligado a su pre-guía, y esta pasa a **En manifiesto**.
- Se acepta una pre-guía Armada. Si está en otro manifiesto, despachada o anulada, se rechaza con el motivo (por
  ejemplo "PG-000123 ya está en el manifiesto N° 45").
- Una fecha de despacho distinta a la del manifiesto genera un aviso, no un error.
- **LPN suelto** que pertenece a una pre-guía activa (escaneo o "Agregar pendientes"): se rechaza con "El LPN X
  pertenece a la pre-guía PG-000123; escanee la hoja". "Agregar pendientes" lo omite y lo cuenta entre los omitidos.
- Quitar del manifiesto:
  - cada transferencia de la pre-guía se puede quitar individualmente;
  - con "Quitar pre-guía" salen todas juntas;
  - cuando ya no queda ninguna en un manifiesto activo, la pre-guía vuelve a **Armada**;
  - anular el manifiesto devuelve sus pre-guías a **Armada**.
- Al despachar el manifiesto, sus pre-guías pasan a **Despachada**.
- En la tabla de documentos del manifiesto, la fila muestra la pre-guía bajo el LPN.

### 3.5 Permisos, menú y avisos
- Menú hoja "Pre-guías" bajo Despachos, con icono `bi-box-seam`, al nivel de Manifiestos. Permiso
  `Despachos.pre-guias`:
  - View: ver e imprimir;
  - Create: generar;
  - Edit: agregar o quitar LPN;
  - Delete: anular.

  Bodega solo recibe este permiso y despacho solo el de manifiestos.
- Campana (`TryEnqueueAsync`): al generar, aviso a los destinatarios configurados de Despachos con el texto
  "N pre-guías listas para el dd-MM (S sucursales)", enlazado al listado filtrado.

## 4. Restricciones (reglas del portal)
- Layout calcado de páginas hermanas (Manifiestos/Index y Detalle), URLs con `~/`, responsive a 375 px
  (`table-responsive-lg`, `btn btn-*`), menú con icono e InvariantCulture en inputs numéricos.
- Migración EF en Postgres y SQL Server, aplicada en `ps_comdepor_dsp_qa` y `ps_comdepor_dsp`.
- No se usan tablas del add-on (`@INX_*`). El control lo lleva el portal por DocEntry (objeto 67).
- La regla "un LPN activo en una sola pre-guía" se valida en el servicio. Si el motor lo permite, también se valida con
  índice único filtrado.

## 5. Decisiones que tomé (revisar)
- La pre-guía guarda la foto de los datos y la revalidación contra SAP se hace al escanearla en el manifiesto. La
  alternativa era no revalidar: sale más rápido, pero arriesga emitir la guía de una transferencia anulada.
- El número **PG-** es fijo, no configurable. Supone que ningún LPN real empieza con "PG-".
- Sin corte automático por máximo de LPN: el corte es el momento de generar, como se describió.
- Una pre-guía rechazada a medias igual entra con lo válido. La alternativa, todo o nada, bloquearía el camión por un
  solo LPN anulado.

## 6. Lo que queda al constructor
Nombres de tablas y clases, diseño exacto del PDF dentro del estilo del manifiesto, y cómo mostrar la vista previa
agrupada (tarjeta por sucursal o tabla con subtotales).
