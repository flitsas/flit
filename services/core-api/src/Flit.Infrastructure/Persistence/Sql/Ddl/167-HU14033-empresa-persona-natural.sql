-- =============================================================================
-- HU #14033 (Épica #14021) — empresa «Persona Natural».
-- Migración: 20261029180000_HU14033_EmpresaPersonaNatural.
--
-- El DDL 152 la sembraba y se retiró para crearla a mano por ambiente. En DEV la fila
-- ya existe y el portal funciona con ella. Vuelve a la migración, idempotente: si el id
-- ya está, no cambia nada. code lo asigna identity.fn_next_tenant_code() (COMP-XXXX).
-- Sin datos personales.
-- =============================================================================

INSERT INTO identity.tenants (id, legal_name, tax_id, tenant_type, is_active, created_at)
VALUES ('00000000-0000-0000-0000-0000000f1171', 'Persona Natural', 'PN-0000000001', 'RENTING', true, now())
ON CONFLICT (id) DO NOTHING;
