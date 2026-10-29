# Facturas de Proveedor y Facturas Reserva de Proveedor en el motor genérico de Compras

Fecha: 2026-10-29 · Estado: diseño para aprobación

## Por qué

El área de compras registra hoy en SAP dos documentos sobre la misma tabla `OPCH`:

- **Factura de proveedores**: para las **facturas de servicios**. Se digita sin documento base, con líneas de
  Servicio (cuenta mayor; ejemplo real: Mercado Libre, cuenta 61-17-001-04, CLP).
- **Factura reserva de acreedores**: para **recibir productos**. Nace del Pedido (Oferta → Pedido →
  Factura de reserva; comentario de SAP "Basado en Pedidos 10005322") con líneas de Artículo, almacén y la
  moneda del proveedor (ejemplo real: MADDEN INTERNATIONAL, USD, sin folio por ser importación). Después,
  bodega hace la Entrada de mercancías contra esa reserva.

Las dos llevan los datos del documento tributario del proveedor: `FolioPref` (43), `FolioNum` (1157501),
`Indicator` ("Factura Electrónica") y `LicTradNum` (RUT).

El portal debe ofrecer ambos documentos dentro del motor genérico de Compras, al lado de Ofertas y Pedidos.
"Copiar de Pedido" es la pieza que después usará la Etapa 2 de Facturas de Proveedor para cargar sola una
factura aprobada con el detalle de la Orden de compra.

## Metas

1. Dos tipos nuevos en el motor genérico de Compras, cada uno con su listado, formulario y entrada de menú.
2. Bloque **Documento tributario** (Indicador, prefijo, N.° de folio y RUT) en los dos.
3. **Copiar de Pedido** en los dos: traer las líneas pendientes de uno o más Pedidos abiertos del proveedor,
   dejando cada línea vinculada a su Pedido en SAP.

## Fuera de alcance

- El flujo de aprobación de Órdenes de compra (todavía por definir, lo dijo el usuario).
- La Etapa 2: ingresar sola a SAP la factura aprobada en la bandeja. Solo se deja lista la pieza que va a
  reutilizar.
- La Entrada de mercancías contra la reserva (la hace bodega en SAP).
- "Copiar de" en Ventas e Inventario: se anota como brecha (ver Paridad).
- Notas de crédito/débito de proveedor y editar una factura ya creada (el motor es solo de creación).

## Anti-metas (sería un fracaso aunque funcione)

- **Que los tipos nuevos reimplementen el formulario.** Regla dura del proyecto: todo nace del documento
  padre. Cada tipo es una línea en `PurchaseDocumentTypeCatalog` más una subclase de unas 20 líneas.
  Toda particularidad (tipo de línea por defecto, documento tributario, "Copiar de") la decide la base a
  partir del catálogo; ningún subtipo sobrescribe `OnGetAsync`/`OnPostAsync`.
- **Que el layout se aparte de los formularios base.** Mismo chrome de pestañas, mismo editor de líneas,
  mismo Resumen total, mismos borradores, mismo "Validar" contra SAP, mismo "Personalizar" y mismo
  listado con filtros, paginador y pestaña Borradores que Pedidos.
- **Que una factura de reserva se cree como factura común**, o al revés, porque comparten tabla y recurso.
- **Registrar dos veces la misma factura del proveedor.**

## Restricciones del proyecto que aplican

- Todo nace del documento padre; paridad entre los motores genéricos (ver CLAUDE.md).
- URLs internas con `~/`; todo responsive a 375 px; diseño copiado de las páginas hermanas.
- Todo menú lleva icono.
- Carpetas de páginas sin colisión con otros plugins: Ventas ya tiene `Pages/ReserveInvoices/`. Si Compras
  usara el mismo nombre, las dos páginas se pisarían en silencio y una daría 404 (memoria "Plugin 404 =
  colisión de ruta Razor").
- SQL en dialecto HANA, como el resto del motor (`HanaToSqlServerTranslator` lo lleva a SQL Server).
- No requiere migraciones: todo vive en SAP.

## Comportamiento

### 1. Los dos tipos

| | Factura de Proveedor | Factura Reserva de Proveedor |
|---|---|---|
| Enum `PurchaseDocumentType` | `PurchaseInvoice` | `PurchaseReserveInvoice` |
| Tabla / recurso Service Layer | `OPCH` / `PurchaseInvoices` | `OPCH` / `PurchaseInvoices` |
| ObjectCode (series) | 18 | 18 |
| Distinción al listar | `isIns = 'N'` | `isIns = 'Y'` |
| Marca al crear | ninguna | `ReserveInvoice = tYES` |
| Tipo de línea al abrir "nuevo" | Servicio | Artículo |
| Documento tributario | sí | sí |
| Copiar de | Pedido | Pedido |
| Se puede crear por defecto | sí | sí |
| Cancelar | sí (como las facturas de Ventas) | sí |
| Menú (código / ruta / icono) | `facturas` / `/compras/facturas-proveedor` / `bi bi-receipt` | `facturasreserva` / `/compras/facturas-reserva` / `bi bi-receipt-cutoff` |
| Carpeta de páginas | `Pages/PurchaseInvoices/` | `Pages/PurchaseReserveInvoices/` |

Para listar y para crear se usan dos datos distintos. El filtro de listado es la columna de la base de datos
(`isIns`) y la marca de creación es la propiedad de Service Layer (`ReserveInvoice`). Ventas hoy envía
`isIns` en el POST. Si Service Layer lo rechaza, la Factura de reserva de Ventas tiene el mismo problema
(ver "Por confirmar").

El tipo de línea por defecto es solo el valor inicial: el usuario puede cambiarlo, igual que hoy.

### 2. Listado

Es el mismo listado de Pedidos. En los tipos con documento tributario se agregan una columna **Folio**
(prefijo-número) y un filtro **Folio** (texto parcial, como los demás). Ofertas y Pedidos no cambian.

### 3. Formulario nuevo

- **Pestaña Finanzas**, el mismo mecanismo de Ventas (`AccountingView`, título "Finanzas"). Aparece solo en
  los tipos con documento tributario y contiene el bloque **Documento tributario**:
  - **Indicador**: lista leída de SAP (tabla de indicadores `OIDC`), vacía por defecto.
  - **Prefijo** y **N.° de folio**.
  - **RUT**: se precarga con el `LicTradNum` del proveedor al elegirlo y se puede editar.
- **El bloque es opcional**, porque la reserva de importación no lo trae. Pero si se llena el prefijo o el
  folio, se exigen los dos y también el Indicador.
- **Duplicados:** si ya existe en `OPCH` una factura no cancelada del mismo proveedor con el mismo prefijo y
  folio, no deja crear y avisa qué documento es (N.° SAP). Revisa los dos tipos, porque comparten tabla.
- **Todo lo demás es idéntico a Pedidos:**
  - Proveedor, fechas, serie, comprador, moneda y lista de precio, con valores por defecto del proveedor.
  - Editor de líneas de Artículo o Servicio, con cuenta mayor y centros de costo.
  - Borradores con "Guardar sin enviar", "Validar" contra SAP y "Personalizar".
  - Permisos por tipo, como Pedidos.
- **Formulario personalizado:** los campos del documento tributario no se pueden ocultar (decisión mía, ver
  abajo).

### 4. Copiar de Pedido

- Botón **"Copiar de Pedido"** junto al editor de líneas. Se ve solo en un documento nuevo de un tipo con
  "Copiar de", y se habilita cuando ya hay proveedor elegido.
- Abre un panel lateral (mismo patrón de drawer que el resto del portal) con los **Pedidos abiertos de ese
  proveedor que tienen líneas pendientes**: N.° SAP, fecha, N.° referencia, moneda, total y cantidad de
  líneas pendientes. Se pueden marcar uno o varios, siempre de la misma moneda.
- **Al copiar:**
  - Se agregan las líneas pendientes de cada Pedido marcado. Cada línea trae artículo o descripción,
    **cantidad pendiente**, precio, % de descuento, almacén o cuenta mayor y centros de costo, y moneda.
  - Cada línea queda con su vínculo `BaseType = 22`, `BaseEntry` y `BaseLine`.
  - El formulario toma la **moneda** del Pedido, y el **comprador** si estaba vacío.
  - En Comentarios se agrega "Basado en Pedidos N.° …", como lo escribe SAP.
- **Tipo de línea:** el tipo de línea del documento pasa a ser el del Pedido. No se mezclan Pedidos de
  Artículo con Pedidos de Servicio, ni líneas copiadas con líneas digitadas de otro tipo. Si se intenta,
  avisa y no copia.
- **Edición:** las líneas copiadas se pueden borrar, y se puede bajar la cantidad para facturar una parte.
  El precio se puede cambiar. No se puede **superar la cantidad pendiente**. Se pueden agregar líneas
  digitadas del mismo tipo.
- **Cambio de proveedor:** quita las líneas copiadas, porque el vínculo dejaría de ser válido.
- **Al crear o validar:** se vuelve a leer en SAP la cantidad pendiente de cada línea vinculada. Si otro
  documento la consumió entretanto, error en esa línea y no crea.
- **Borradores:** el vínculo con el Pedido se guarda en el borrador y sobrevive al reabrirlo.
- **Etapa 2:** la lectura de Pedidos abiertos y de sus líneas pendientes vive en Core, en
  `IPurchaseDocumentService`, en Abstractions. Así el plugin Facturas de Proveedor la reutiliza en la Etapa 2
  sin depender de Modulo.Compras.

### 5. Ver una factura existente

Es el mismo detalle de solo lectura de Pedidos, con la pestaña Finanzas mostrando el documento tributario
leído de SAP. Muestra Cerrar y Cancelar con el mismo criterio que hoy.

### 6. Importación Genérica

Importación Genérica recorre `Enum.GetValues<PurchaseDocumentType>()`, así que los dos tipos aparecen solos
como destino. Se les da nombre en `ImportDisplayNames`:
- "Factura de proveedor" / "Purchase invoice".
- "Factura de reserva de proveedor" / "Purchase reserve invoice".

Mapear el documento tributario desde el Excel queda fuera de esta entrega. Los campos de usuario de SAP sí
siguen disponibles, como hoy.

## Por confirmar contra SAP (se prueba con "Validar", que simula sin crear)

1. Que Service Layer acepte `ReserveInvoice = tYES` en `PurchaseInvoices` y que deje `isIns = 'Y'`.
   Probar también la Factura de reserva de Ventas: si `isIns` en el POST falla, corregirla con el mismo
   mecanismo.
2. Los nombres de las propiedades en Service Layer: `FolioPrefixString`, `FolioNumber`, `Indicator`,
   `FederalTaxID` (supongo).
3. ObjectCode 18 de `OPCH`, cruzando las series reales (`SA_FAPRO`) contra `NNM1`, como se hizo con 22.
4. Que SAP acepte una línea con `BaseType`/`BaseEntry`/`BaseLine` hacia un Pedido de compra y con la
   cantidad menor a la pendiente.

## Paridad con Ventas e Inventario

- **"Copiar de" no se lleva ahora a Ventas** (Factura de reserva desde Orden de venta) **ni a Inventario**
  (Traslado desde Solicitud). Se anota la brecha en `docs/08-BRECHA-FUNCIONAL-VS-PORTALSAP-V2.md`. El
  pedido del usuario es de Compras.
- **Documento tributario:** no aplica a Ventas, porque FACELE asigna el folio al emitir.
- **Columna y filtro Folio:** no aplican a Inventario, que no tiene folio de proveedor.

## Decisiones que tomé yo (revisar)

1. **Ubicación del documento tributario:** va en una pestaña Finanzas, copiando la de Ventas, y no en la
   cabecera como lo muestra SAP. Así se mantiene el layout del formulario base.
2. **Campos no ocultables:** el documento tributario queda fuera de "Personalizar", para que nadie lo
   esconda por error.
3. **Bloqueo de duplicados:** no deja crear una factura con un folio ya registrado para el mismo proveedor.
4. **Copiar varios Pedidos:** se pueden copiar varios a la vez, siempre de la misma moneda y del mismo tipo
   de línea.
5. **Cambio de proveedor:** borra las líneas copiadas.
6. **Detalle de una factura existente:** no muestra el Pedido de origen por línea; queda en Comentarios.

## Lo que queda a criterio de quien lo construya

- Nombres exactos de métodos y records nuevos en `IPurchaseDocumentService`, del catálogo de indicadores y
  de los campos que suma `PurchaseDocumentTypeCatalog.Entry`.
- Cómo arma el panel "Copiar de Pedido" (fetch a un handler de la base, como `OnGetPartnerDefaultsAsync`).
- Las pruebas: cómo arma el cuerpo del POST (marca de reserva, documento tributario, vínculo con el Pedido),
  el catálogo, y las validaciones de cantidad pendiente, duplicado y folio incompleto.

## Documentación a actualizar

- CLAUDE.md: la frase "Compras no tiene tabs Logística/Finanzas" y "Ninguno de los 2 tipos comparte tabla"
  pasan a describir los 4 tipos.
- `docs/08-BRECHA-FUNCIONAL-VS-PORTALSAP-V2.md`: Compras con facturas; brecha de "Copiar de" en Ventas e
  Inventario.
- Comentarios de `PurchaseDocumentType`, `PurchaseDocumentTypeCatalog`, `IPurchaseDocumentService` y
  `PurchaseDocumentDto` que hoy dicen "2 tipos" y "sin Copy-From".
