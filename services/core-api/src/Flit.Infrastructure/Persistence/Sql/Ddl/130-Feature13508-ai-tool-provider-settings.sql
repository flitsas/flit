-- ─────────────────────────────────────────────────────────────────────────────
-- Feature #13508 (Épica #13272) — Configuración multi-proveedor de IA por herramienta.
-- ADR-0070 (Propuesto). Migración EF: 20261012200000_Feature13508_AiToolProviderSettings.
--
-- Qué crea (schema admin, GLOBAL de plataforma: sin tenant_id, sin RLS — mismo precedente
-- que admin.banners / admin.notification_test_settings / admin.mandate_format_settings):
--   1. admin.ai_tool_provider_settings — activo + auto_return + providers_json por tool
--   2. admin.ai_provider_transitions — historial append-only (manual / failover / seed)
--   3. admin.ai_failover_alerts — alertas in-app 1:1 con transición de failover
--
-- Secretos NUNCA en BD: solo env (ANTHROPIC_API_KEY, OPENAI_API_KEY, KIMI_API_KEY,
-- DEEPSEEK_API_KEY). Soft delete: no (settings = update in place; transitions/alerts append/ack).
-- IDEMPOTENTE: IF NOT EXISTS / DROP TRIGGER IF EXISTS / INSERT … WHERE NOT EXISTS.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS admin.ai_tool_provider_settings (
    id                      uuid          NOT NULL DEFAULT uuidv7(),
    tool_code               varchar(32)   NOT NULL,
    active_provider         varchar(32)   NOT NULL,
    auto_return_to_primary  boolean       NOT NULL DEFAULT true,
    -- JSON array ordenado: [{"providerCode":"anthropic","enabled":true,"priorityRank":1}, ...]
    providers_json          jsonb         NOT NULL,
    row_version             bigint        NOT NULL DEFAULT 0,
    created_at              timestamptz   NOT NULL DEFAULT now(),
    created_by              uuid,
    updated_at              timestamptz,
    updated_by              uuid,

    CONSTRAINT pk_ai_tool_provider_settings PRIMARY KEY (id),
    CONSTRAINT uq_ai_tool_provider_settings_tool UNIQUE (tool_code),
    CONSTRAINT ck_ai_tool_provider_settings_tool
        CHECK (tool_code IN ('ocr', 'dr_flit')),
    CONSTRAINT ck_ai_tool_provider_settings_active
        CHECK (active_provider IN ('anthropic', 'openai', 'kimi', 'deepseek'))
);

DROP TRIGGER IF EXISTS tr_ai_tool_provider_settings_row_version
  ON admin.ai_tool_provider_settings;
CREATE TRIGGER tr_ai_tool_provider_settings_row_version
  BEFORE UPDATE ON admin.ai_tool_provider_settings
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_ai_tool_provider_settings_audit
  ON admin.ai_tool_provider_settings;
CREATE TRIGGER tr_ai_tool_provider_settings_audit
  AFTER INSERT OR UPDATE OR DELETE ON admin.ai_tool_provider_settings
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

COMMENT ON TABLE admin.ai_tool_provider_settings IS
  'Feature #13508 / Épica #13272 — configuración multi-proveedor IA por herramienta (activo, prioridad, auto_return). GLOBAL de plataforma sin RLS (ADR-0070). Secretos solo en env.';
COMMENT ON COLUMN admin.ai_tool_provider_settings.tool_code IS
  'Catálogo en código: ocr | dr_flit (extensible vía migración + CHECK).';
COMMENT ON COLUMN admin.ai_tool_provider_settings.active_provider IS
  'Proveedor activo actual: anthropic | openai | kimi | deepseek.';
COMMENT ON COLUMN admin.ai_tool_provider_settings.providers_json IS
  'Array JSON ordenado de {providerCode, enabled, priorityRank}. No contiene API keys.';
COMMENT ON COLUMN admin.ai_tool_provider_settings.row_version IS
  'Token de concurrencia optimista; lo incrementa tr_ai_tool_provider_settings_row_version.';

CREATE TABLE IF NOT EXISTS admin.ai_provider_transitions (
    id              uuid          NOT NULL DEFAULT uuidv7(),
    tool_code       varchar(32)   NOT NULL,
    from_provider   varchar(32),
    to_provider     varchar(32)   NOT NULL,
    reason          varchar(32)   NOT NULL,
    detail          varchar(1000),
    actor_user_id   uuid,
    occurred_at     timestamptz   NOT NULL DEFAULT now(),

    CONSTRAINT pk_ai_provider_transitions PRIMARY KEY (id),
    CONSTRAINT ck_ai_provider_transitions_reason
        CHECK (reason IN ('manual', 'failover_auto', 'auto_return', 'seed')),
    CONSTRAINT ck_ai_provider_transitions_tool
        CHECK (tool_code IN ('ocr', 'dr_flit')),
    CONSTRAINT ck_ai_provider_transitions_to
        CHECK (to_provider IN ('anthropic', 'openai', 'kimi', 'deepseek')),
    CONSTRAINT ck_ai_provider_transitions_from
        CHECK (from_provider IS NULL OR from_provider IN ('anthropic', 'openai', 'kimi', 'deepseek'))
);

CREATE INDEX IF NOT EXISTS ix_ai_provider_transitions_tool_occurred
    ON admin.ai_provider_transitions (tool_code, occurred_at DESC);

-- Append-only: el historial debe poder reconstruir qué proveedor estuvo activo.
CREATE OR REPLACE FUNCTION admin.trg_ai_provider_transitions_immutable() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'admin.ai_provider_transitions es append-only: no se permite %', TG_OP
        USING ERRCODE = 'restrict_violation';
END;
$$;

DROP TRIGGER IF EXISTS tr_ai_provider_transitions_immutable ON admin.ai_provider_transitions;
CREATE TRIGGER tr_ai_provider_transitions_immutable
  BEFORE UPDATE OR DELETE ON admin.ai_provider_transitions
  FOR EACH ROW EXECUTE FUNCTION admin.trg_ai_provider_transitions_immutable();

COMMENT ON TABLE admin.ai_provider_transitions IS
  'Feature #13508 — historial append-only de cambios de proveedor (manual, failover_auto, auto_return, seed).';

CREATE TABLE IF NOT EXISTS admin.ai_failover_alerts (
    id              uuid          NOT NULL DEFAULT uuidv7(),
    transition_id   uuid          NOT NULL,
    tool_code       varchar(32)   NOT NULL,
    from_provider   varchar(32)   NOT NULL,
    to_provider     varchar(32)   NOT NULL,
    occurred_at     timestamptz   NOT NULL DEFAULT now(),
    acked_at        timestamptz,
    acked_by        uuid,

    CONSTRAINT pk_ai_failover_alerts PRIMARY KEY (id),
    CONSTRAINT fk_ai_failover_alerts_transition
        FOREIGN KEY (transition_id) REFERENCES admin.ai_provider_transitions (id)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT uq_ai_failover_alerts_transition UNIQUE (transition_id),
    CONSTRAINT ck_ai_failover_alerts_tool
        CHECK (tool_code IN ('ocr', 'dr_flit'))
);

CREATE INDEX IF NOT EXISTS ix_ai_failover_alerts_acked_occurred
    ON admin.ai_failover_alerts (acked_at NULLS FIRST, occurred_at DESC);

COMMENT ON TABLE admin.ai_failover_alerts IS
  'Feature #13508 — alertas in-app de failover automático (1:1 con transición). ack = acked_at/acked_by.';

-- Seed: una fila por tool con prioridad default Anthropic → OpenAI → Kimi → DeepSeek.
INSERT INTO admin.ai_tool_provider_settings (
    tool_code, active_provider, auto_return_to_primary, providers_json
)
SELECT
    v.tool_code,
    'anthropic',
    true,
    '[
      {"providerCode":"anthropic","enabled":true,"priorityRank":1},
      {"providerCode":"openai","enabled":true,"priorityRank":2},
      {"providerCode":"kimi","enabled":true,"priorityRank":3},
      {"providerCode":"deepseek","enabled":true,"priorityRank":4}
    ]'::jsonb
FROM (VALUES ('ocr'), ('dr_flit')) AS v(tool_code)
WHERE NOT EXISTS (
    SELECT 1 FROM admin.ai_tool_provider_settings s WHERE s.tool_code = v.tool_code
);

INSERT INTO admin.ai_provider_transitions (tool_code, from_provider, to_provider, reason, detail)
SELECT v.tool_code, NULL, 'anthropic', 'seed', 'Seed Feature #13508 — proveedor inicial anthropic'
FROM (VALUES ('ocr'), ('dr_flit')) AS v(tool_code)
WHERE NOT EXISTS (
    SELECT 1
    FROM admin.ai_provider_transitions t
    WHERE t.tool_code = v.tool_code AND t.reason = 'seed'
);
