-- ─────────────────────────────────────────────────────────────────────────────
-- Registro de consumo de APIs de IA — contrato ia-consumo v1 (docs/contratos/ia-consumo.v1.md, ADR-0090).
-- Migración EF: 20261119120000_IaConsumoTablas.
--
-- Qué crea (schema admin, GLOBAL de plataforma: tenant_id es texto informativo, sin RLS — mismo precedente
-- que admin.ai_tool_provider_settings, ADR-0070):
--   1. admin.ia_consumo_log     — una fila por cada llamada HTTP real al proveedor (éxito o fallo)
--   2. admin.ia_consumo_detalle — prompt/respuesta completos, solo con IA_CONSUMO_DETALLE=true (retención N días)
--   3. admin.ia_tarifa_modelo   — USD por millón de tokens para el costo estimado
--
-- La estructura es EXACTAMENTE la del contrato (la comparten FLITO y los demás servicios): no agregar columnas
-- ni CHECKs de catálogo aquí sin subir el contrato a v2. Nunca se guarda el archivo, su base64, llaves ni
-- encabezados. Sin triggers de auditoría: es una bitácora append-only de alto volumen.
-- IDEMPOTENTE: IF NOT EXISTS / INSERT … ON CONFLICT DO NOTHING.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS admin.ia_consumo_log (
  id                      uuid PRIMARY KEY,
  correlacion_id          uuid        NOT NULL,
  intento                 smallint    NOT NULL DEFAULT 1,
  pasada                  smallint    NOT NULL DEFAULT 1,
  fecha_inicio            timestamptz NOT NULL,
  fecha_fin               timestamptz NOT NULL,
  latencia_ms             integer     NOT NULL,
  aplicativo              text        NOT NULL,
  servicio                text        NOT NULL,
  ambiente                text        NOT NULL,
  tenant_id               text        NULL,
  usuario_id              text        NULL,
  usuario_login           text        NULL,
  origen                  text        NOT NULL,
  modulo                  text        NOT NULL,
  proceso                 text        NOT NULL,
  tipo_tarea              text        NOT NULL,
  placa                   text        NULL,
  entidad_tipo            text        NULL,
  entidad_id              text        NULL,
  archivo_mime            text        NULL,
  archivo_bytes           bigint      NULL,
  archivo_paginas         integer     NULL,
  archivo_sha256          text        NULL,
  proveedor               text        NOT NULL,
  modelo_solicitado       text        NULL,
  modelo_respondido       text        NULL,
  id_respuesta_proveedor  text        NULL,
  max_tokens              integer     NULL,
  prompt_codigo           text        NULL,
  prompt_version          text        NULL,
  prompt_hash             text        NULL,
  http_status             integer     NOT NULL DEFAULT 0,
  resultado               text        NOT NULL,
  stop_reason             text        NULL,
  error_codigo            text        NULL,
  error_mensaje           text        NULL,
  tokens_entrada          integer     NOT NULL DEFAULT 0,
  tokens_salida           integer     NOT NULL DEFAULT 0,
  tokens_cache_lectura    integer     NOT NULL DEFAULT 0,
  tokens_cache_escritura  integer     NOT NULL DEFAULT 0,
  costo_usd_estimado      numeric(12,6) NULL,
  metadatos               jsonb       NULL
);
CREATE INDEX IF NOT EXISTS ix_ia_consumo_log_fecha        ON admin.ia_consumo_log (fecha_inicio);
CREATE INDEX IF NOT EXISTS ix_ia_consumo_log_modulo_fecha ON admin.ia_consumo_log (modulo, fecha_inicio);
CREATE INDEX IF NOT EXISTS ix_ia_consumo_log_placa        ON admin.ia_consumo_log (placa) WHERE placa IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_ia_consumo_log_usuario      ON admin.ia_consumo_log (usuario_id, fecha_inicio) WHERE usuario_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_ia_consumo_log_correlacion  ON admin.ia_consumo_log (correlacion_id);

COMMENT ON TABLE admin.ia_consumo_log IS
  'Contrato ia-consumo v1 (ADR-0090) — una fila por llamada HTTP real a un proveedor de IA. GLOBAL sin RLS; tenant_id informativo.';

CREATE TABLE IF NOT EXISTS admin.ia_consumo_detalle (
  consumo_id      uuid PRIMARY KEY REFERENCES admin.ia_consumo_log(id) ON DELETE CASCADE,
  fecha_inicio    timestamptz NOT NULL,
  prompt_texto    text NULL,
  respuesta_texto text NULL
);
CREATE INDEX IF NOT EXISTS ix_ia_consumo_detalle_fecha ON admin.ia_consumo_detalle (fecha_inicio);

COMMENT ON TABLE admin.ia_consumo_detalle IS
  'Contrato ia-consumo v1 — prompt y respuesta completos (64 KB c/u). Solo con IA_CONSUMO_DETALLE=true; purga diaria a IA_CONSUMO_DETALLE_DIAS.';

CREATE TABLE IF NOT EXISTS admin.ia_tarifa_modelo (
  id                         serial PRIMARY KEY,
  proveedor                  text          NOT NULL,
  modelo                     text          NOT NULL,
  vigente_desde              date          NOT NULL,
  precio_entrada_mtok        numeric(12,6) NOT NULL,
  precio_salida_mtok         numeric(12,6) NOT NULL,
  precio_cache_lectura_mtok  numeric(12,6) NOT NULL DEFAULT 0,
  precio_cache_escritura_mtok numeric(12,6) NOT NULL DEFAULT 0,
  moneda                     text          NOT NULL DEFAULT 'USD',
  UNIQUE (proveedor, modelo, vigente_desde)
);

COMMENT ON TABLE admin.ia_tarifa_modelo IS
  'Contrato ia-consumo v1 — USD por millón de tokens. Sin tarifa el costo queda NULL (no 0).';

-- Semilla: SOLO Anthropic, tabla oficial de precios (USD/MTok; escritura de caché = la de 5 minutos, 1,25× la
-- entrada). openai / kimi / deepseek no se siembran (costo NULL hasta confirmar precio). Claude Haiku 5.5 tiene dos
-- tramos según el tamaño del prompt; se siembra el de ≤100K tokens, igual que FLITO (un prompt mayor queda
-- subestimado: es un costo ESTIMADO).
INSERT INTO admin.ia_tarifa_modelo
  (proveedor, modelo, vigente_desde, precio_entrada_mtok, precio_salida_mtok, precio_cache_lectura_mtok, precio_cache_escritura_mtok)
VALUES
  ('anthropic', 'claude-haiku-4-5',  DATE '2026-11-18',  1.00,  5.00, 0.10,  1.25),
  ('anthropic', 'claude-sonnet-4-6', DATE '2026-11-18',  3.00, 15.00, 0.30,  3.75),
  ('anthropic', 'claude-haiku-5-5',  DATE '2026-11-18',  0.10,  0.50, 0.01,  0.125),
  ('anthropic', 'claude-sonnet-5',  DATE '2026-11-18',  2.00, 10.00, 0.20,  2.50),
  ('anthropic', 'claude-sonnet-5-5', DATE '2026-11-18',  2.00, 10.00, 0.10,  2.50),
  ('anthropic', 'claude-opus-4-6',   DATE '2026-11-18',  5.00, 25.00, 0.50,  6.25),
  ('anthropic', 'claude-opus-4-7',   DATE '2026-11-18',  5.00, 25.00, 0.50,  6.25),
  ('anthropic', 'claude-opus-4-8',   DATE '2026-11-18',  5.00, 25.00, 0.50,  6.25),
  ('anthropic', 'claude-opus-5',     DATE '2026-11-18',  5.00, 25.00, 0.50,  6.25),
  ('anthropic', 'claude-opus-5-5',   DATE '2026-11-18',  4.00, 20.00, 0.20,  5.00),
  ('anthropic', 'claude-fable-5-2',  DATE '2026-11-18',  8.00, 40.00, 0.20, 10.00)
ON CONFLICT (proveedor, modelo, vigente_desde) DO NOTHING;
