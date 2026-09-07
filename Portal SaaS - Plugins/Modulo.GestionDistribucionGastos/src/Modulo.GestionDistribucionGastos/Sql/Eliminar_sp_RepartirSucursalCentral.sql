-- Elimina el reparto manual "a demanda" de Sucursal Central (IND4) -- botones "Repartir saldos en
-- Sucursal Central" / "Deshacer reparto Sucursal Central" en Pages/GestionGastos/Reglas/Index.cshtml.
-- Reemplazado por la regla automática 'SUCURSAL_DESDE_IND4_POR_CANAL' (ver
-- Sql/vw_VentaBaseDistribucion_Final.sql / Sql/sp_EjecutarDistribucionAutomatica_Final.sql /
-- Sql/Catalogo_BaseDistribucion.sql), que hace lo mismo -- repartir por venta del mismo canal lo
-- que quedó en IND4 -- pero integrado al motor de reglas por cuenta, en vez de una acción global
-- manual sobre TipoOrigen MANUAL/SIN_AJUSTE sin distinguir cuenta.
-- Correr esto DESPUÉS de confirmar que ya no queda nada pendiente de esos dos botones (si hay
-- filas con ReglaAplicada LIKE 'Reparto Sucursal Central (IND4)%' que se quieran deshacer, hacerlo
-- ANTES de borrar el procedimiento, porque después ya no va a estar disponible el botón para eso).
DROP PROCEDURE IF EXISTS dbo.sp_RepartirSucursalCentral;
DROP PROCEDURE IF EXISTS dbo.sp_DeshacerRepartoSucursalCentral;
