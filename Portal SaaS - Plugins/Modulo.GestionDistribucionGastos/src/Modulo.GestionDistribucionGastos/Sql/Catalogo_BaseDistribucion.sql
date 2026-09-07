-- Catálogo de bases de distribución disponibles para el combo de Reglas_Distribucion
-- (Pages/GestionGastos/Reglas/Form.cshtml). Antes las opciones estaban hardcodeadas en el
-- <select> del Razor -- agregar una base nueva (ej. SUCURSAL_COLISEUM, SUCURSAL_DESDE_IND4_POR_CANAL)
-- obligaba a tocar HTML además de la vista/SP. Ahora agregar una base nueva es solo un INSERT acá
-- (o correr de nuevo este script agregando la fila) -- el Form la lee dinámicamente.
-- El "Codigo" DEBE coincidir exactamente con el TipoBase que produce vw_VentaBaseDistribucion.
-- Idempotente: se puede correr de nuevo sin duplicar filas ni pisar el Activo/Orden manual
-- que alguien haya cambiado desde la base.
IF OBJECT_ID('dbo.Catalogo_BaseDistribucion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Catalogo_BaseDistribucion (
        Codigo      VARCHAR(50)  NOT NULL PRIMARY KEY,
        Descripcion VARCHAR(200) NOT NULL,
        Orden       INT          NOT NULL,
        Activo      BIT          NOT NULL DEFAULT 1
    );
END;

MERGE dbo.Catalogo_BaseDistribucion AS destino
USING (VALUES
    ('CANAL',                         'Canal',                                                                              10),
    ('SUCURSAL',                      'Sucursal',                                                                           20),
    ('SUCURSAL_TPR_ECM',              'Sucursal (solo TPR y ECM)',                                                          30),
    ('SUCURSAL_TPR_SOLO_ECOM',        'Sucursal (TPR y solo Ecommerce propio, sin marketplace)',                            40),
    ('SUCURSAL_COLISEUM',             'Sucursal (solo tiendas Coliseum)',                                                   50),
    ('SUCURSAL_DESDE_IND4_POR_CANAL', 'Sucursal (reparte lo asignado a Central/IND4, solo entre sucursales del mismo canal, por venta)', 60),
    ('VENTA_SUCURSAL_CONVERSE',       'Sucursal (venta Converse 41-01-001-04)',                                             70),
    ('VENTA_SUCURSAL_UMBRO',          'Sucursal (venta Umbro 41-01-001-05)',                                                80),
    ('VENTA_SUCURSAL_FILA',           'Sucursal (venta Fila 41-01-001-06)',                                                 90),
    ('VENTA_SUCURSAL_SM',             'Sucursal (venta SM 41-01-001-09)',                                                  100),
    ('VENTA_SUCURSAL_CONVERSE_DESDE_IND4_POR_CANAL', 'Sucursal (reparte Central/IND4 solo entre sucursales que venden Converse, por canal)', 110),
    ('VENTA_SUCURSAL_UMBRO_DESDE_IND4_POR_CANAL',    'Sucursal (reparte Central/IND4 solo entre sucursales que venden Umbro, por canal)',    120),
    ('VENTA_SUCURSAL_FILA_DESDE_IND4_POR_CANAL',     'Sucursal (reparte Central/IND4 solo entre sucursales que venden Fila, por canal)',     130),
    ('VENTA_SUCURSAL_SM_DESDE_IND4_POR_CANAL',       'Sucursal (reparte Central/IND4 solo entre sucursales que venden SM, por canal)',        140),
    ('SUCURSAL_COLISEUM_DESDE_IND4_POR_CANAL',       'Sucursal (reparte Central/IND4 solo entre tiendas Coliseum, por canal)',                150)
) AS origen (Codigo, Descripcion, Orden)
ON destino.Codigo = origen.Codigo
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Codigo, Descripcion, Orden, Activo) VALUES (origen.Codigo, origen.Descripcion, origen.Orden, 1)
WHEN MATCHED AND (destino.Descripcion <> origen.Descripcion OR destino.Orden <> origen.Orden) THEN
    UPDATE SET Descripcion = origen.Descripcion, Orden = origen.Orden;
