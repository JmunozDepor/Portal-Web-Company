-- Verificacion de la migracion InstanceAsModuleDatabaseServer (SQL Server, T-SQL).
-- Equivalente de verificacion-instance-as-module-db.sql. SOLO en una base desechable.
-- Correr con sqlcmd -I (QUOTED_IDENTIFIER ON; sin -I el DELETE falla por indices filtrados).
-- Pasos: migrar a AllowMultipleImportFormatsPerPartner; correr hasta "===== Despues"; aplicar
-- InstanceAsModuleDatabaseServer; correr el resto. Ver el .sql de Postgres para la explicacion del escenario.

-- ===== Preludio: SOLO para base de verificacion VACIA =====
INSERT INTO organizations (id, legal_name, country, mode, status, created_at, slug)
VALUES ('11111111-1111-1111-1111-111111111111', 'VERIF Org', 'CL', 'saas', 'active', SYSDATETIMEOFFSET(), 'verif-org');
INSERT INTO instances (organization_id, name, host, port, engine_type, technical_username, technical_secret_key, is_active)
VALUES ('11111111-1111-1111-1111-111111111111', 'VERIF hana', 'hana.verif.local', 30015, 'hana', 'verif', 'verif', 1);
INSERT INTO companies (id, organization_id, instance_id, code, name, database_name, service_layer_url, integration_username, integration_secret_key, country, is_active)
SELECT '22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', i.id, 'VERIF', 'VERIF Co', 'VERIF_DB', 'https://verif.local', 'u', 's', 'CL', 1
FROM instances i WHERE i.host = 'hana.verif.local';
-- ===== Fin preludio =====
GO

WITH comp AS (SELECT TOP 1 id FROM companies ORDER BY code)
INSERT INTO company_external_connections (company_id, nombre, tipo, host, port, database_name, technical_username, technical_secret_key, is_active, created_at, updated_at)
SELECT comp.id, v.nombre, v.tipo, v.host, v.port, v.db, v.usr, v.sec, v.act, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET()
FROM comp, (VALUES
  ('VERIF-A', 'db_postgres', '10.0.0.9', 5432, 'ps_a', 'app', 'cifrado-1', 1),
  ('VERIF-B', 'db_postgres', '10.0.0.9', 5432, 'ps_b', 'app', 'cifrado-2', 1),
  ('VERIF-C', 'db_postgres', '10.0.0.8', 5432, 'ps_c', 'app', 'cifrado-3', 0),
  ('VERIF-D', 'http_api',    NULL,       NULL, NULL,   NULL,  NULL,        1),
  ('VERIF-E', 'db_hana',     'hana.verif.local', 30015, 'HDB_E', 'verif', 'verif', 1),
  ('VERIF-F', 'db_postgres', '10.0.0.7', NULL, 'ps_f', 'app', NULL, 1),
  ('VERIF-G', 'db_postgres', '10.0.0.9', 5432, 'ps_g', 'app', 'cifrado-1', 1),
  ('VERIF-H', 'db_postgres', 'servidor-con-nombre-muy-largo-para-verificar.verif.local', 5432, 'ps_h', 'usuario-uno', 'k1', 1),
  ('VERIF-I', 'db_postgres', 'servidor-con-nombre-muy-largo-para-verificar.verif.local', 5432, 'ps_i', 'usuario-dos', 'k2', 1)
) AS v(nombre, tipo, host, port, db, usr, sec, act);

WITH comp AS (SELECT TOP 1 id FROM companies ORDER BY code)
INSERT INTO company_module_connections (company_id, module_code, purpose, connection_id, updated_at)
SELECT comp.id, 'VERIF_' + SUBSTRING(x.nombre, 7, 50), CASE WHEN x.nombre = 'VERIF-G' THEN 'Otro' ELSE 'Default' END, x.id, SYSDATETIMEOFFSET()
FROM comp JOIN company_external_connections x ON x.company_id = comp.id AND x.nombre LIKE 'VERIF-%';
GO

-- ===== Despues de aplicar InstanceAsModuleDatabaseServer, correr esto: =====
-- Esperado: 5 instancias sin nombres duplicados ('VERIF hana', 10.0.0.7 con clave vacia, 10.0.0.9,
-- y dos del host largo con #n distintos); 10.0.0.8 no aparece; E no crea instancia.
SELECT id, name, host, port, technical_username, CASE WHEN technical_secret_key = '' THEN 1 ELSE 0 END AS clave_vacia FROM instances ORDER BY id;
-- Esperado: total=5, distintos=5, max_len<=50.
SELECT COUNT(*) AS total, COUNT(DISTINCT name) AS distintos, MAX(LEN(name)) AS max_len FROM instances;
-- Esperado: 6 filas (A, B, E, F, H, I). B con usuario 'app' + clave propia; E -> 'VERIF hana'; F ambas NULL.
SELECT b.module_code, i.name AS instancia, b.database_name, b.technical_username, CASE WHEN b.technical_secret_key IS NOT NULL THEN 1 ELSE 0 END AS tiene_clave
FROM company_module_connections b JOIN instances i ON i.id = b.instance_id
WHERE b.module_code LIKE 'VERIF_%' ORDER BY b.module_code;
-- Esperado: 0 filas.
SELECT module_code FROM company_module_connections
WHERE (CASE WHEN technical_username IS NULL THEN 1 ELSE 0 END) <> (CASE WHEN technical_secret_key IS NULL THEN 1 ELSE 0 END);
GO

-- Limpieza (incluye el preludio).
DELETE FROM company_module_connections WHERE module_code LIKE 'VERIF_%';
DELETE FROM companies WHERE id = '22222222-2222-2222-2222-222222222222';
DELETE FROM instances WHERE organization_id = '11111111-1111-1111-1111-111111111111';
DELETE FROM organizations WHERE id = '11111111-1111-1111-1111-111111111111';
GO
-- Comprobaciones manuales (deben fallar con "Invalid object name"):
--   SELECT 1 FROM company_external_connections;
--   SELECT 1 FROM module_external_connections;
