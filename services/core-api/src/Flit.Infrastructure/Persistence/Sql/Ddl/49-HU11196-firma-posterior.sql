-- HU #11196 (Feature #11188, otros-ajustes) — Firma a posteriori: marca por trámite y aplicación por lote.
--
-- Cuando el representante legal de una empresa tiene la identidad Y la firma del baúl vencidas, el
-- trámite no puede firmarse hoy. En vez de dejar al gestor bloqueado, el trámite se MARCA: cuando esa
-- persona complete su validación de identidad, todos sus trámites marcados se firman de una.
--
-- Por qué una tabla y no una columna en procedure_instances: la marca necesita estado propio
-- (pendiente → aplicada) y trazabilidad de la aplicación (cuándo y con qué validación, AC6). Una
-- columna booleana no puede contar qué pasó después.
--
-- La llave del lote es (tenant_id, representante) — el DOCUMENTO de la persona, no un id del
-- directorio de Admin: en el caso principal de la HU #11195 el representante NO está registrado ahí,
-- así que un representative_id no existiría. El NIT de la compañía representada se guarda para la
-- traza, no para filtrar el lote.
-- DDL IDEMPOTENTE.

CREATE TABLE IF NOT EXISTS tramites.deferred_signature_marks (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_deferred_signature_marks PRIMARY KEY (id),
    tenant_id uuid NOT NULL,
    procedure_instance_id uuid NOT NULL
        CONSTRAINT fk_dsm_procedure_instance
        REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    -- Parte del trámite cuya firma se difiere: comprador | vendedor.
    party_role text NOT NULL,
    -- NIT de la empresa representada, solo para la traza del lote.
    company_document_number text NOT NULL,
    -- Persona que firmará cuando valide su identidad. Es la llave real del lote.
    representative_document_type text NOT NULL,
    representative_document_number text NOT NULL,
    -- pendiente | aplicada | descartada
    estado text NOT NULL DEFAULT 'pendiente',
    CONSTRAINT ck_deferred_signature_marks_estado
        CHECK (estado IN ('pendiente', 'aplicada', 'descartada')),
    created_at timestamptz NOT NULL DEFAULT now(),
    -- Traza de la aplicación diferida (AC6): cuándo se firmó y con qué validación.
    applied_at timestamptz NULL,
    applied_validation_id uuid NULL,
    -- Por qué se descartó (p. ej. el trámite salió de borrador antes de que llegara la validación).
    discarded_reason text NULL
);

-- Una sola marca PENDIENTE por (trámite, parte). Filtrado por estado para que una marca ya aplicada o
-- descartada no impida volver a marcar el mismo trámite más adelante.
CREATE UNIQUE INDEX IF NOT EXISTS uq_deferred_signature_marks_pendiente
  ON tramites.deferred_signature_marks(procedure_instance_id, party_role)
  WHERE estado = 'pendiente';

-- La consulta del lote: "qué trámites pendientes tiene esta persona en este tenant".
CREATE INDEX IF NOT EXISTS ix_deferred_signature_marks_representante
  ON tramites.deferred_signature_marks(
      tenant_id, representative_document_type, representative_document_number, estado);

-- ── RLS — aislamiento por tenant (igual que el resto del schema tramites) ────────
-- AC5: los trámites marcados de otra empresa gestora quedan fuera del lote por construcción.
ALTER TABLE tramites.deferred_signature_marks ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.deferred_signature_marks;
CREATE POLICY tenant_isolation ON tramites.deferred_signature_marks
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
