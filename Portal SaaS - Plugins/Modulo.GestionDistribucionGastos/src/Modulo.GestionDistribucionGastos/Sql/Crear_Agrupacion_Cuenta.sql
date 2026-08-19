-- dbo/Sql/Crear_Agrupacion_Cuenta.sql
-- Todas las cuentas del sistema, con la clasificación de negocio asignada (si la
-- tiene). Se sincroniza automáticamente desde la pantalla "Agrupación de Cuentas"
-- (MERGE, ver AgrupacionCuentas/Index.cshtml.cs) -- no se carga a mano. Ejecutar una
-- sola vez contra la base externa (CLDEPORFIN).
CREATE TABLE dbo.Agrupacion_Cuenta (
    NroCuenta            VARCHAR(50)  NOT NULL PRIMARY KEY,
    NombreCuenta         VARCHAR(200) NULL,
    ClasificacionId      INT NULL,
    FechaModificacion    DATETIME NOT NULL DEFAULT GETDATE(),
    UsuarioModificacion  VARCHAR(100) NULL,
    CONSTRAINT FK_Agrupacion_Cuenta_Clasificacion FOREIGN KEY (ClasificacionId)
        REFERENCES dbo.Clasificacion_Cuenta(Id)
);
