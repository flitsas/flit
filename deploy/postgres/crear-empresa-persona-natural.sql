-- HU #14033 (Feature #14022, Épica #14021 Portal Persona Natural): crea la empresa «Persona Natural».
--
-- La siembra vive en la migración 20261029180000_HU14033_EmpresaPersonaNatural (mismo INSERT, idempotente).
-- Este script queda para repetirlo a mano si hace falta, ANTES de encender la fachada
-- (PERSONA_NATURAL_FACHADA_HABILITADA=true). Ver docs/suite/handoff-vps-personanatural.md, §3.
--
-- Qué hace: inserta UNA fila en identity.tenants con el id fijo de Flit.Platform.Sdk.PlatformTenants.PersonaNatural.
-- Todo trámite del portal nace a nombre de esta empresa; core-api solo acepta que el servicio del portal
-- (svc-personanatural) actúe para ella.
--
-- Cómo correrlo (con un usuario que pueda escribir en identity.tenants, el dueño del esquema de core-api):
--
--   psql "$CONNECTION_STRING_CORE_ADMIN" -v ON_ERROR_STOP=1 -f deploy/postgres/crear-empresa-persona-natural.sql
--
-- Verificar: debe devolver una fila con code COMP-XXXX y is_active = true.
--
--   psql "$CONNECTION_STRING_CORE_ADMIN" -c "SELECT id, code, legal_name, is_active FROM identity.tenants WHERE id = '00000000-0000-0000-0000-0000000f1171'"
--
-- Qué NO hace: no toca ninguna compañía existente, no concede organismos de tránsito ni módulos a la empresa y no crea
-- usuarios (los organismos habilitados para el portal se conceden aparte, como a cualquier compañía).
--
-- Idempotente: si la fila ya existe (mismo id) no cambia nada; correrlo de nuevo es seguro. La migración
-- 20261029180000_HU14033_EmpresaPersonaNatural hace este mismo INSERT.
--
-- Datos de la fila:
--   code         lo asigna la secuencia de la Épica #13517 (DEFAULT de identity.tenants.code, COMP-XXXX).
--   tax_id       identificador propio, no numérico, para no chocar con ningún NIT real (uq_tenants_tax_id es único).
--   tenant_type  RENTING: compañía B2B común, sin cabeza de grupo (is_group_parent = false). Cambiarlo es cosa del PO.
--
-- Revertir (solo si la empresa no tiene nada colgado: trámites, usuarios, permisos; si lo tiene, la llave foránea lo
-- impide y no se pierde nada):
--   DELETE FROM identity.tenants WHERE id = '00000000-0000-0000-0000-0000000f1171';

\set ON_ERROR_STOP on

INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
VALUES ('00000000-0000-0000-0000-0000000f1171', 'Persona Natural', 'PN-0000000001', 'RENTING', true, now())
ON CONFLICT DO NOTHING;
