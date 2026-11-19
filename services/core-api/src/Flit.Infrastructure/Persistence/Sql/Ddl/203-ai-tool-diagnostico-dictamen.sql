-- ─────────────────────────────────────────────────────────────────────────────
-- Ola «LLM de Diagnóstico desde Configuración de IA» — HU-5 [DATABASE] (registro diferido).
-- ADR-0091 (Propuesto), enmienda ADR-0070 (Propuesto). Migración EF:
-- 20261119041051_HU5_AiToolDiagnosticoDictamen.
--
-- Qué hace (schema admin, GLOBAL de plataforma: sin tenant_id ni RLS, mismo precedente que DDL 130):
--   1. Amplía a 'diagnostico_dictamen' el CHECK de tool_code de las 3 tablas que lo restringen:
--      admin.ai_tool_provider_settings, admin.ai_provider_transitions, admin.ai_failover_alerts.
--   2. Siembra la fila de 'diagnostico_dictamen' con active_provider='anthropic' y providers_json con los 4 slots:
--      anthropic habilitado (rank 1); openai/kimi/deepseek enabled=false (ranks 2-4) para que la pantalla los pinte
--      «No aplica» (decisión del usuario 2026-11-18, P1=a: la herramienta solo admite anthropic).
--   3. Registra la transición 'seed' de esa herramienta.
--
-- La llave NO vive en BD: core-api usa la ANTHROPIC_API_KEY general (P2=b). El modelo vive en config.
-- IDEMPOTENTE: DROP CONSTRAINT IF EXISTS + ADD, INSERT … WHERE NOT EXISTS. Reversible: Down de la migración.
-- ─────────────────────────────────────────────────────────────────────────────

ALTER TABLE admin.ai_tool_provider_settings
    DROP CONSTRAINT IF EXISTS ck_ai_tool_provider_settings_tool;
ALTER TABLE admin.ai_tool_provider_settings
    ADD CONSTRAINT ck_ai_tool_provider_settings_tool
    CHECK (tool_code IN ('ocr', 'dr_flit', 'diagnostico_dictamen'));

ALTER TABLE admin.ai_provider_transitions
    DROP CONSTRAINT IF EXISTS ck_ai_provider_transitions_tool;
ALTER TABLE admin.ai_provider_transitions
    ADD CONSTRAINT ck_ai_provider_transitions_tool
    CHECK (tool_code IN ('ocr', 'dr_flit', 'diagnostico_dictamen'));

ALTER TABLE admin.ai_failover_alerts
    DROP CONSTRAINT IF EXISTS ck_ai_failover_alerts_tool;
ALTER TABLE admin.ai_failover_alerts
    ADD CONSTRAINT ck_ai_failover_alerts_tool
    CHECK (tool_code IN ('ocr', 'dr_flit', 'diagnostico_dictamen'));

COMMENT ON COLUMN admin.ai_tool_provider_settings.tool_code IS
  'Catálogo en código: ocr | dr_flit | diagnostico_dictamen (extensible vía migración + CHECK).';

-- Seed: diagnostico_dictamen solo habilita anthropic; los otros 3 slots van deshabilitados (P1=a).
INSERT INTO admin.ai_tool_provider_settings (
    tool_code, active_provider, auto_return_to_primary, providers_json
)
SELECT
    'diagnostico_dictamen',
    'anthropic',
    true,
    '[
      {"providerCode":"anthropic","enabled":true,"priorityRank":1},
      {"providerCode":"openai","enabled":false,"priorityRank":2},
      {"providerCode":"kimi","enabled":false,"priorityRank":3},
      {"providerCode":"deepseek","enabled":false,"priorityRank":4}
    ]'::jsonb
WHERE NOT EXISTS (
    SELECT 1 FROM admin.ai_tool_provider_settings s WHERE s.tool_code = 'diagnostico_dictamen'
);

INSERT INTO admin.ai_provider_transitions (tool_code, from_provider, to_provider, reason, detail)
SELECT 'diagnostico_dictamen', NULL, 'anthropic', 'seed',
       'Seed HU-5 ola LLM Diagnóstico — proveedor inicial anthropic (único admitido)'
WHERE NOT EXISTS (
    SELECT 1
    FROM admin.ai_provider_transitions t
    WHERE t.tool_code = 'diagnostico_dictamen' AND t.reason = 'seed'
);
