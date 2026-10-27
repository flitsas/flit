-- =============================================================================
-- HU #14033 (Feature #14022, Épica #14021) — la empresa «Persona Natural».
-- Migración: 20261027170000_HU14033_EmpresaPersonaNatural.
--
-- Qué hace:
--   Siembra UNA fila en identity.tenants con el id fijo de Flit.Platform.Sdk.PlatformTenants.PersonaNatural. Todo
--   trámite del portal de persona natural nace a nombre de esta empresa; core-api solo acepta que el servicio del portal
--   (svc-personanatural) actúe para ella (PersonaNaturalServicioAuthorization.VerificarEmpresa).
--
-- Qué NO hace: no toca ninguna compañía existente, no concede organismos de tránsito ni módulos a la empresa y no crea
--   usuarios. Aditiva: si la fila ya existe (mismo id) no cambia nada.
--
-- Datos de la fila:
--   code        lo asigna la secuencia de la Épica #13517 (DEFAULT de identity.tenants.code, COMP-XXXX).
--   tax_id      identificador propio, no numérico, para no chocar con ningún NIT real (uq_tenants_tax_id es único).
--   tenant_type RENTING: compañía B2B común, sin cabeza de grupo (is_group_parent = false). Cambiarlo es cosa del PO.
-- Idempotente: ON CONFLICT (id) DO NOTHING.
-- =============================================================================

INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
VALUES ('00000000-0000-0000-0000-0000000f1171', 'Persona Natural', 'PN-0000000001', 'RENTING', true, now())
ON CONFLICT (id) DO NOTHING;
