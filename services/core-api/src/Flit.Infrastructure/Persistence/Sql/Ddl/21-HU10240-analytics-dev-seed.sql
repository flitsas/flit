-- HU #10240 (DEV-ONLY) — Seed analítico desde trámites sintéticos | Feature #10139
-- ⚠️  DEV-ONLY: genera procedure_instances + status_history sintéticos para el
--     tenant DEV en ~35 días y luego invoca analytics.refresh_procedure_aggregates
--     para poblar las tablas analytics. NO usar en producción.
--     GUIDs fijos reutilizados de 12-HU10200-dev-seed.sql:
--       DEV_TENANT_ID = 11111111-1111-1111-1111-111111111111
--       DEV_USER_ID   = 22222222-2222-2222-2222-222222222222
-- Idempotente:
--   · instancias  → ON CONFLICT (tenant_id, reference_number) DO NOTHING
--   · historial   → WHERE NOT EXISTS por (instancia, to_status)
--   · agregados   → la función hace upsert (ON CONFLICT DO UPDATE)
-- Re-ejecutable sin duplicar filas.

-- ─────────────────────────────────────────────────────────────────────────────
-- 0. Asegurar tenant/user DEV (idempotente). Normalmente ya los siembra
--    12-HU10200-dev-seed.sql (migración previa, mismo gate de entorno); se
--    re-aseguran aquí para que el seed sea autocontenido.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, created_at)
VALUES ('11111111-1111-1111-1111-111111111111', 'FLITDEV', 'Flit Dev Tenant',
        '900000000-0', 'FLIT', true, now())
ON CONFLICT (id) DO NOTHING;

INSERT INTO identity.users (id, email, display_name, status, created_at)
VALUES ('22222222-2222-2222-2222-222222222222', 'dev@flitsas.io', 'Usuario Dev', 'active', now())
ON CONFLICT (id) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Instancias sintéticas: día × tipo × variante(estado) × copias.
--    35 días (offset 0..34) → ≥30 fechas distintas (AC1).
-- ─────────────────────────────────────────────────────────────────────────────
WITH dev AS (
    SELECT '11111111-1111-1111-1111-111111111111'::uuid AS tenant_id,
           '22222222-2222-2222-2222-222222222222'::uuid AS user_id
),
types AS (
    -- Un tipo por family (categoría) entre los tipos activos; índice estable fidx.
    SELECT id, family, row_number() OVER (ORDER BY family, code) AS fidx
    FROM (
        SELECT DISTINCT ON (pt.family) pt.id, pt.family, pt.code
        FROM tramites.procedure_types pt
        WHERE pt.is_active = true
        ORDER BY pt.family, pt.code
    ) one_per_family
),
days AS (
    SELECT gs AS day_offset, (current_date - gs) AS metric_date
    FROM generate_series(0, 34) gs
),
variants AS (
    SELECT * FROM (VALUES
        (1, 'draft',       2, false, false, false),
        (2, 'submitted',   3, true,  false, false),
        (3, 'in_review',   1, true,  false, false),
        (4, 'approved_ot', 2, true,  true,  false),
        (5, 'rejected_ot', 1, true,  false, true )
    ) AS v(variant, status, n_copies, do_submit, do_approve, do_reject)
),
combos AS (
    SELECT d.metric_date, t.id AS procedure_type_id, t.fidx,
           v.variant, v.status, v.do_submit, v.do_approve, v.do_reject, c.copy
    FROM days d
    CROSS JOIN types t
    CROSS JOIN variants v
    CROSS JOIN LATERAL generate_series(1, v.n_copies) AS c(copy)
)
INSERT INTO tramites.procedure_instances
    (id, tenant_id, procedure_type_id, reference_number, status,
     submitted_at, completed_at, created_by_user_id, created_at)
SELECT uuidv7(),
       dev.tenant_id,
       c.procedure_type_id,
       'SEED-ANL-' || to_char(c.metric_date, 'YYYYMMDD') || '-' || c.fidx || '-' || c.variant || '-' || c.copy,
       c.status,
       CASE WHEN c.do_submit THEN (c.metric_date + time '10:00')::timestamptz END,
       CASE WHEN c.do_approve OR c.do_reject THEN (c.metric_date + time '12:00')::timestamptz END,
       dev.user_id,
       (c.metric_date + time '09:00')::timestamptz
FROM combos c
CROSS JOIN dev
ON CONFLICT (tenant_id, reference_number) DO NOTHING;

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. Historial de estados (productividad). Derivado de las instancias sembradas.
--    Cada transición usa la fecha del evento (= día de la instancia) y el user DEV.
-- ─────────────────────────────────────────────────────────────────────────────
-- 2a. Transición a 'submitted' para toda instancia que salió de draft.
INSERT INTO tramites.procedure_instance_status_history
    (id, tenant_id, procedure_instance_id, from_status, to_status, changed_at, changed_by)
SELECT uuidv7(), pi.tenant_id, pi.id, 'draft', 'submitted',
       COALESCE(pi.submitted_at, pi.created_at), pi.created_by_user_id
FROM tramites.procedure_instances pi
WHERE pi.tenant_id = '11111111-1111-1111-1111-111111111111'
  AND pi.reference_number LIKE 'SEED-ANL-%'
  AND pi.status IN ('submitted', 'in_review', 'approved_ot', 'rejected_ot')
  AND NOT EXISTS (
      SELECT 1 FROM tramites.procedure_instance_status_history h
      WHERE h.procedure_instance_id = pi.id AND h.to_status = 'submitted'
  );

-- 2b. Transición a 'approved_ot'.
INSERT INTO tramites.procedure_instance_status_history
    (id, tenant_id, procedure_instance_id, from_status, to_status, changed_at, changed_by)
SELECT uuidv7(), pi.tenant_id, pi.id, 'in_review', 'approved_ot',
       COALESCE(pi.completed_at, pi.created_at), pi.created_by_user_id
FROM tramites.procedure_instances pi
WHERE pi.tenant_id = '11111111-1111-1111-1111-111111111111'
  AND pi.reference_number LIKE 'SEED-ANL-%'
  AND pi.status = 'approved_ot'
  AND NOT EXISTS (
      SELECT 1 FROM tramites.procedure_instance_status_history h
      WHERE h.procedure_instance_id = pi.id AND h.to_status = 'approved_ot'
  );

-- 2c. Transición a 'rejected_ot'.
INSERT INTO tramites.procedure_instance_status_history
    (id, tenant_id, procedure_instance_id, from_status, to_status, changed_at, changed_by)
SELECT uuidv7(), pi.tenant_id, pi.id, 'in_review', 'rejected_ot',
       COALESCE(pi.completed_at, pi.created_at), pi.created_by_user_id
FROM tramites.procedure_instances pi
WHERE pi.tenant_id = '11111111-1111-1111-1111-111111111111'
  AND pi.reference_number LIKE 'SEED-ANL-%'
  AND pi.status = 'rejected_ot'
  AND NOT EXISTS (
      SELECT 1 FROM tramites.procedure_instance_status_history h
      WHERE h.procedure_instance_id = pi.id AND h.to_status = 'rejected_ot'
  );

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. Refrescar agregados del tenant DEV para el rango sembrado (AC1 + AC2).
-- ─────────────────────────────────────────────────────────────────────────────
SELECT analytics.refresh_procedure_aggregates(
    '11111111-1111-1111-1111-111111111111'::uuid,
    current_date - 34,
    current_date
);

-- ─────────────────────────────────────────────────────────────────────────────
-- Verificación post-seed (DEV)
-- ─────────────────────────────────────────────────────────────────────────────
-- SELECT count(DISTINCT metric_date) FROM analytics.procedure_metrics_daily
--   WHERE tenant_id = '11111111-1111-1111-1111-111111111111';  -- esperado: >= 30
-- SELECT count(DISTINCT metric_date) FROM analytics.user_productivity_daily
--   WHERE tenant_id = '11111111-1111-1111-1111-111111111111';  -- esperado: >= 30
