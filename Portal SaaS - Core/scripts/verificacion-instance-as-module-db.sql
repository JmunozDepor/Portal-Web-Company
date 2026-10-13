-- Verificacion de la migracion InstanceAsModuleDatabaseServer (Postgres).
-- Usar SOLO en una base de verificacion desechable (nunca dev/real). Pasos:
--   1) Llevar la base a AllowMultipleImportFormatsPerPartner.
--   2) Correr este script hasta la linea "===== Despues ...".
--   3) Aplicar InstanceAsModuleDatabaseServer.
--   4) Correr el resto.

-- ===== Preludio: SOLO para base de verificacion VACIA =====
-- Una base recien migrada no tiene companias: se crea organizacion + instancia hana + compania minimas.
-- Si la base ya tiene companias, omitir este bloque (y ajustar la limpieza final).
INSERT INTO organizations (id, legal_name, country, mode, status, created_at, slug)
VALUES ('11111111-1111-1111-1111-111111111111', 'VERIF Org', 'CL', 'saas', 'active', now(), 'verif-org');
INSERT INTO instances (organization_id, name, host, port, engine_type, technical_username, technical_secret_key, is_active)
VALUES ('11111111-1111-1111-1111-111111111111', 'VERIF hana', 'hana.verif.local', 30015, 'hana', 'verif', 'verif', TRUE);
INSERT INTO companies (id, organization_id, instance_id, code, name, database_name, service_layer_url, integration_username, integration_secret_key, country, is_active)
SELECT '22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', i.id, 'VERIF', 'VERIF Co', 'VERIF_DB', 'https://verif.local', 'u', 's', 'CL', TRUE
FROM instances i WHERE i.host = 'hana.verif.local';
-- ===== Fin preludio =====

-- Escenario (modulo = VERIF_<letra>):
--   A, B: mismo servidor/usuario, claves cifradas distintas -> 1 instancia; B queda con credencial propia (usuario+clave).
--   C: conexion inactiva -> binding descartado.   D: http_api -> descartado.
--   E: db_hana activa igual a la instancia del preludio -> se REUTILIZA (no crea instancia), sin credencial propia.
--   F: db_postgres con port NULL y clave NULL -> instancia puerto 5432, clave de instancia '', binding con ambas credenciales NULL.
--   G: igual servidor que A/B pero binding con purpose='Otro' -> descartado.
--   H, I: host largo (prefijo de 42 chars identico), distinto usuario -> dos instancias con nombres distintos.
WITH comp AS (SELECT id FROM companies ORDER BY code LIMIT 1)
INSERT INTO company_external_connections (company_id, nombre, tipo, host, port, database_name, technical_username, technical_secret_key, is_active, created_at, updated_at)
SELECT comp.id, v.nombre, v.tipo, v.host, v.port, v.db, v.usr, v.sec, v.act, now(), now()
FROM comp, (VALUES
  ('VERIF-A', 'db_postgres', '10.0.0.9', 5432, 'ps_a', 'app', 'cifrado-1', TRUE),
  ('VERIF-B', 'db_postgres', '10.0.0.9', 5432, 'ps_b', 'app', 'cifrado-2', TRUE),
  ('VERIF-C', 'db_postgres', '10.0.0.8', 5432, 'ps_c', 'app', 'cifrado-3', FALSE),
  ('VERIF-D', 'http_api',    NULL,       NULL, NULL,   NULL,  NULL,        TRUE),
  ('VERIF-E', 'db_hana',     'hana.verif.local', 30015, 'HDB_E', 'verif', 'verif', TRUE),
  ('VERIF-F', 'db_postgres', '10.0.0.7', NULL, 'ps_f', 'app', NULL, TRUE),
  ('VERIF-G', 'db_postgres', '10.0.0.9', 5432, 'ps_g', 'app', 'cifrado-1', TRUE),
  ('VERIF-H', 'db_postgres', 'servidor-con-nombre-muy-largo-para-verificar.verif.local', 5432, 'ps_h', 'usuario-uno', 'k1', TRUE),
  ('VERIF-I', 'db_postgres', 'servidor-con-nombre-muy-largo-para-verificar.verif.local', 5432, 'ps_i', 'usuario-dos', 'k2', TRUE)
) AS v(nombre, tipo, host, port, db, usr, sec, act);

WITH comp AS (SELECT id FROM companies ORDER BY code LIMIT 1)
INSERT INTO company_module_connections (company_id, module_code, purpose, connection_id, updated_at)
SELECT comp.id, 'VERIF_' || substr(x.nombre, 7), CASE WHEN x.nombre = 'VERIF-G' THEN 'Otro' ELSE 'Default' END, x.id, now()
FROM comp JOIN company_external_connections x ON x.company_id = comp.id AND x.nombre LIKE 'VERIF-%';

-- ===== Despues de aplicar InstanceAsModuleDatabaseServer, correr esto: =====
-- Esperado (5 filas, sin duplicados de nombre):
--   'VERIF hana' (la del preludio, SIN instancia nueva para E);
--   'postgres 10.0.0.9:5432 app #n' (1 sola; C y G no crean otra);
--   'postgres 10.0.0.7:5432 app #n' (puerto por defecto, technical_secret_key = '');
--   dos instancias 'postgres servidor-con-nombre-muy-largo-pa... #n' con #n distintos.
--   10.0.0.8 (C inactiva) NO aparece.
SELECT id, name, host, port, technical_username, technical_secret_key = '' AS clave_vacia FROM instances ORDER BY id;
-- Esperado: nombres distintos entre si y todos <= 50 chars.
SELECT count(*) AS total, count(DISTINCT name) AS distintos, max(length(name)) AS max_len FROM instances;
-- Esperado (6 filas; faltan VERIF_C, VERIF_D y VERIF_G):
--   VERIF_A -> instancia 10.0.0.9, ps_a,  sin credencial propia
--   VERIF_B -> instancia 10.0.0.9, ps_b,  usuario 'app' + clave propia (tiene_clave=t)
--   VERIF_E -> instancia 'VERIF hana' (el id del preludio), HDB_E, sin credencial propia
--   VERIF_F -> instancia 10.0.0.7, ps_f, ambas credenciales NULL
--   VERIF_H / VERIF_I -> instancias distintas, ps_h / ps_i, sin credencial propia (cada una es su unica fila)
SELECT b.module_code, i.name AS instancia, b.database_name, b.technical_username, b.technical_secret_key IS NOT NULL AS tiene_clave
FROM company_module_connections b JOIN instances i ON i.id = b.instance_id
WHERE b.module_code LIKE 'VERIF_%' ORDER BY b.module_code;
-- Esperado: 0 filas (nunca username sin clave ni al reves).
SELECT module_code FROM company_module_connections WHERE (technical_username IS NULL) <> (technical_secret_key IS NULL);

-- Limpieza (tambien borra el preludio).
DELETE FROM company_module_connections WHERE module_code LIKE 'VERIF_%';
DELETE FROM companies WHERE id = '22222222-2222-2222-2222-222222222222';
DELETE FROM instances WHERE organization_id = '11111111-1111-1111-1111-111111111111';
DELETE FROM organizations WHERE id = '11111111-1111-1111-1111-111111111111';

-- Comprobaciones manuales (cada una debe fallar con 'relation ... does not exist'):
--   SELECT 1 FROM company_external_connections;
--   SELECT 1 FROM module_external_connections;
