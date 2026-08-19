-- Limpieza acordada con el usuario el 2026-07-15 tras auditar qué tablas/vistas usa realmente la app
-- (CLAUDE.md sección 4/6). Verificado antes de borrar: 0 filas, sin referencias en el código C#,
-- en ninguno de los 4 stored procedures, ni en sys.sql_expression_dependencies.

DROP VIEW IF EXISTS dbo.vw_PendientesManual;
DROP TABLE IF EXISTS dbo.Correccion_Dimension_Venta;
