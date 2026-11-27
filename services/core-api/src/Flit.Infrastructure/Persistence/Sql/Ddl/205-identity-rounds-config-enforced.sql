-- ─────────────────────────────────────────────────────────────────────────────
-- Configurador GLOBAL de rondas de validación de identidad (Kyverum). Migración EF: 20261124100000_IdentityRoundsConfig.
--
-- Una ronda = una fila de tramites.procedure_instance_biometric_validations (hasta 3 intentos de Kyverum dentro del mismo
-- enlace). Un reenvío abre la siguiente ronda: rondas usadas = 1 + resend_count (por FILA, no por persona).
--
-- Schema admin, GLOBAL de plataforma (sin tenant_id ni RLS, mismo precedente que admin.ia_consumo_log):
--   1. admin.identity_rounds_config       — UNA sola fila (id = 1 por CHECK): tope de rondas, disparador y correos de alerta.
--   2. admin.identity_rounds_alert_audit  — una fila por alerta enviada. UNIQUE (validation_id, ronda) = guardia de idempotencia:
--      un webhook repetido o una reconciliación no mandan dos correos para la misma ronda.
--
-- Permiso identity_rounds.manage: solo el rol SuperAdmin (lo siembra DevelopmentAuthSeeder); aquí se marca enforced = 'excluded'.
-- IDEMPOTENTE: IF NOT EXISTS / ON CONFLICT DO NOTHING / UPDATE solo si cambia.
-- ─────────────────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS admin.identity_rounds_config (
  id                smallint    PRIMARY KEY DEFAULT 1,
  rondas_maximas    integer     NOT NULL DEFAULT 3,
  disparador_alerta text        NOT NULL DEFAULT 'al_maximo',
  correos_alerta    text[]      NOT NULL DEFAULT '{}',
  updated_at        timestamptz NOT NULL DEFAULT now(),
  updated_by        uuid        NULL,
  CONSTRAINT ck_identity_rounds_config_single   CHECK (id = 1),
  CONSTRAINT ck_identity_rounds_config_rondas   CHECK (rondas_maximas BETWEEN 1 AND 10),
  CONSTRAINT ck_identity_rounds_config_disparo  CHECK (disparador_alerta IN ('al_maximo', 'cada_ronda')),
  CONSTRAINT ck_identity_rounds_config_correos  CHECK (cardinality(correos_alerta) <= 20)
);

COMMENT ON TABLE admin.identity_rounds_config IS
  'Configuración GLOBAL (una sola fila) de rondas de validación de identidad: tope, disparador de alerta y correos. Solo SuperAdmin FLIT.';

INSERT INTO admin.identity_rounds_config (id, rondas_maximas, disparador_alerta, correos_alerta)
VALUES (1, 3, 'al_maximo', '{}')
ON CONFLICT (id) DO NOTHING;

CREATE TABLE IF NOT EXISTS admin.identity_rounds_alert_audit (
  id                    uuid        PRIMARY KEY,
  validation_id         uuid        NOT NULL,
  procedure_instance_id uuid        NULL,
  ronda                 integer     NOT NULL,
  disparador            text        NOT NULL,
  destinatarios         text[]      NOT NULL DEFAULT '{}',
  fecha                 timestamptz NOT NULL DEFAULT now(),
  resultado             text        NOT NULL DEFAULT 'pendiente',
  CONSTRAINT ck_identity_rounds_alert_ronda      CHECK (ronda >= 1),
  CONSTRAINT ck_identity_rounds_alert_disparador CHECK (disparador IN ('al_maximo', 'cada_ronda')),
  CONSTRAINT ck_identity_rounds_alert_resultado  CHECK (resultado IN ('pendiente', 'enviado', 'parcial', 'fallido', 'sin_destinatarios')),
  CONSTRAINT uq_identity_rounds_alert_validation_ronda UNIQUE (validation_id, ronda)
);

CREATE INDEX IF NOT EXISTS ix_identity_rounds_alert_fecha ON admin.identity_rounds_alert_audit (fecha DESC);

COMMENT ON TABLE admin.identity_rounds_alert_audit IS
  'Auditoría de las alertas de rondas de identidad agotadas. UNIQUE (validation_id, ronda): una alerta por ronda como máximo. Sin FK a la validación para que la auditoría sobreviva a su borrado. No guarda el contenido del correo.';

-- Marcador enforced (mismo formato que 204-HU14648-runt-contingency-enforced.sql; lo lee PermisosEnforcedArchitectureTests).
UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES
        ('identity_rounds.manage', 'excluded')
       ) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;
