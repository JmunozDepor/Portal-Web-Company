-- ===== Preludio: SOLO para base de verificacion VACIA (scratch, nunca una base real) =====
-- Una base recien migrada no tiene companias; se crea una organizacion + instancia + compania minimas.
-- Si la base ya tiene companias, omitir este bloque.
INSERT INTO organizations (id, legal_name, country, mode, status, created_at, slug)
VALUES ('11111111-1111-1111-1111-111111111111', 'VERIF Org', 'CL', 'saas', 'active', now(), 'verif-org');
INSERT INTO instances (organization_id, name, host, port, engine_type, technical_username, technical_secret_key, is_active)
VALUES ('11111111-1111-1111-1111-111111111111', 'VERIF hana', 'hana.verif.local', 30015, 'hana', 'verif', 'verif', TRUE);
INSERT INTO companies (id, organization_id, instance_id, code, name, database_name, service_layer_url, integration_username, integration_secret_key, country, is_active)
SELECT '22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', i.id, 'VERIF', 'VERIF Co', 'VERIF_DB', 'https://verif.local', 'u', 's', 'CL', TRUE
FROM instances i WHERE i.host = 'hana.verif.local';
-- ===== Fin preludio =====

-- Correr con la base local en la migración ANTERIOR (AllowMultipleImportFormatsPerPartner).
-- Requiere al menos una compañía en la base local. Escenario:
--   A y B: mismo servidor y usuario, misma clave en claro pero cifrado distinto (se
--          simula con textos distintos) → 1 instancia; B queda con credencial propia hasta
--          que el normalizador la evalúe.
--   C: conexión inactiva → su binding se descarta.
--   D: http_api → su binding se descarta.
WITH comp AS (SELECT id FROM companies ORDER BY code LIMIT 1)
INSERT INTO company_external_connections (company_id, nombre, tipo, host, port, database_name, technical_username, technical_secret_key, is_active, created_at, updated_at)
SELECT comp.id, v.nombre, v.tipo, v.host, v.port, v.db, v.usr, v.sec, v.act, now(), now()
FROM comp, (VALUES
  ('VERIF-A', 'db_postgres', '10.0.0.9', 5432, 'ps_a', 'app', 'cifrado-1', TRUE),
  ('VERIF-B', 'db_postgres', '10.0.0.9', 5432, 'ps_b', 'app', 'cifrado-2', TRUE),
  ('VERIF-C', 'db_postgres', '10.0.0.8', 5432, 'ps_c', 'app', 'cifrado-3', FALSE),
  ('VERIF-D', 'http_api',    NULL,       NULL, NULL,   NULL,  NULL,        TRUE)
) AS v(nombre, tipo, host, port, db, usr, sec, act);

WITH comp AS (SELECT id FROM companies ORDER BY code LIMIT 1)
INSERT INTO company_module_connections (company_id, module_code, purpose, connection_id, updated_at)
SELECT comp.id, 'VERIF_' || substr(x.nombre, 7), 'Default', x.id, now()
FROM comp JOIN company_external_connections x ON x.company_id = comp.id AND x.nombre LIKE 'VERIF-%';

-- ===== Después de aplicar InstanceAsModuleDatabaseServer, correr esto: =====
-- Esperado: 1 fila, engine postgres, host 10.0.0.9, usuario app.
SELECT name, engine_type, host, port, technical_username FROM instances WHERE host IN ('10.0.0.9', '10.0.0.8');
-- Esperado: 2 filas (VERIF_A, VERIF_B), misma instance_id, bases ps_a / ps_b;
-- VERIF_A sin credencial propia (su clave fue la elegida por MIN), VERIF_B con technical_username='app'.
SELECT module_code, instance_id, database_name, technical_username, technical_secret_key IS NOT NULL AS tiene_clave_propia
FROM company_module_connections WHERE module_code LIKE 'VERIF_%' ORDER BY module_code;
-- Esperado: error "relation does not exist" en las dos.
SELECT 1 FROM company_external_connections;
SELECT 1 FROM module_external_connections;

-- Limpieza.
DELETE FROM company_module_connections WHERE module_code LIKE 'VERIF_%';
DELETE FROM instances WHERE host = '10.0.0.9';
