-- dbo/Sql/Crear_Clasificacion_Cuenta.sql
-- Catálogo de clasificaciones de negocio libres para reportes EERR (solo a nivel de
-- reporte, no afecta distribución). Ejecutar una sola vez contra la base externa
-- (CLDEPORFIN).
CREATE TABLE dbo.Clasificacion_Cuenta (
    Id     INT IDENTITY PRIMARY KEY,
    Codigo VARCHAR(20)  NOT NULL,
    Nombre VARCHAR(100) NOT NULL,
    Orden  INT NOT NULL DEFAULT 0,
    Activo BIT NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Clasificacion_Cuenta_Codigo UNIQUE (Codigo)
);
