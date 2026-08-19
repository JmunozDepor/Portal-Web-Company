-- Bloqueo duro por cuenta/mes: una vez aprobada, ninguna edición (asignación, división,
-- indirecto) se acepta hasta que un aprobador la reabra -- mismo criterio que Cierre_Mes pero
-- a nivel de una cuenta individual en vez de todo el mes. Ejecutar una sola vez contra la base
-- externa (CLDEPORFIN).
CREATE TABLE dbo.Cuenta_Aprobada (
    AnioMes           CHAR(6)      NOT NULL,
    NroCuenta         VARCHAR(50)  NOT NULL,
    UsuarioAprobador  VARCHAR(100) NOT NULL,
    FechaAprobacion   DATETIME     NOT NULL,
    CONSTRAINT PK_Cuenta_Aprobada PRIMARY KEY (AnioMes, NroCuenta)
);
