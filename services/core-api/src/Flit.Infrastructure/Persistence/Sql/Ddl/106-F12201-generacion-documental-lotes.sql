-- Feature #12201 — Incremento I3: carga masiva XLSX (ADR-0056-generacion-documental-standalone).
--
-- Cabecera de lote + ampliación de admin.standalone_documents con el vínculo a ese lote.
-- SIN tabla de items (sub-decisión §3.c del diseño / ADR): una fila fallida del XLSX es un
-- documento con status='error' en admin.standalone_documents, exactamente el mismo estado que ya
-- existe para una generación individual fallida. Una fila del XLSX se vincula al lote por
-- (batch_id, row_number); no hay entidad intermedia.
--
-- POR QUÉ VA EN UN .sql PROPIO Y NO DENTRO DEL 105:
--   1) El 105 se aplica al mergear I1. Una migración ya aplicada a cualquier ambiente NO se puede
--      modificar (regla innegociable 5 del database-agent): ampliar el 105 después sería reescribir
--      historia aplicada.
--   2) I1/I2 e I3 son incrementos independientes con PRs separados (§3 del diseño); mezclarlos
--      obligaría a desplegar el modelo de lotes antes de que exista el worker que lo usa.
--   3) El propio ADR lo fija: «un .sql numerado (105-…) + migración EF; en I3, 106-…».
--
-- DDL IDEMPOTENTE y SIN BEGIN/COMMIT (lo ejecuta la migración EF dentro de su transacción).

-- ════════════════════════════════════════════════════════════════════════════════════════════════
-- admin.standalone_document_batches — cabecera del lote
-- ════════════════════════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS admin.standalone_document_batches (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_standalone_document_batches PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_standalone_document_batches_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    created_by_user_id uuid NOT NULL
        CONSTRAINT fk_standalone_document_batches_user
        REFERENCES identity.users(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    status varchar(20) NOT NULL DEFAULT 'queued'
        CONSTRAINT ck_standalone_document_batches_status
        CHECK (status IN ('queued', 'processing', 'completed', 'partial_failure', 'failed')),

    template_version varchar(10) NOT NULL DEFAULT 'v1',

    -- El XLSX fuente vive en storage, como los PDF. PROHIBIDO BYTEA también aquí.
    source_filename varchar(500) NOT NULL,
    source_storage_path varchar(1000) NOT NULL,
    source_sha256 varchar(64) NOT NULL,

    -- Tope duro de 100 filas por lote: control de costo y de abuso, no solo de UX.
    total_items integer NOT NULL DEFAULT 0
        CONSTRAINT ck_standalone_document_batches_max_100
        CHECK (total_items BETWEEN 0 AND 100),
    generated_count integer NOT NULL DEFAULT 0
        CONSTRAINT ck_standalone_document_batches_generated_count
        CHECK (generated_count >= 0),
    error_count integer NOT NULL DEFAULT 0
        CONSTRAINT ck_standalone_document_batches_error_count
        CHECK (error_count >= 0),

    idempotency_key varchar(120),

    -- Reaper de lotes atascados (R5): quién y cuándo tomó el lote.
    claimed_at timestamptz,
    completed_at timestamptz,

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,

    -- Los contadores no pueden superar el total declarado.
    CONSTRAINT ck_standalone_document_batches_contadores CHECK (
        generated_count + error_count <= total_items
    ),

    -- Estado terminal <=> hay fecha de cierre. Evita lotes «completados» sin cierre auditable.
    CONSTRAINT ck_standalone_document_batches_cierre CHECK (
        (status IN ('completed', 'partial_failure', 'failed') AND completed_at IS NOT NULL)
        OR (status IN ('queued', 'processing') AND completed_at IS NULL)
    )
);

CREATE INDEX IF NOT EXISTS ix_standalone_document_batches_tenant_created
  ON admin.standalone_document_batches (tenant_id, created_at DESC)
  WHERE deleted_at IS NULL;

-- A9: índice propio para la FK a identity.users.
CREATE INDEX IF NOT EXISTS ix_standalone_document_batches_tenant_user
  ON admin.standalone_document_batches (tenant_id, created_by_user_id);

-- Idempotencia del lote (CF-16). Mismo criterio parcial que en standalone_documents.
CREATE UNIQUE INDEX IF NOT EXISTS uq_standalone_document_batches_tenant_idempotency
  ON admin.standalone_document_batches (tenant_id, idempotency_key)
  WHERE idempotency_key IS NOT NULL AND deleted_at IS NULL;

-- Cola del worker. Excepción DELIBERADA a A11 (tenant_id primero): el BackgroundService toma
-- lotes de TODOS los tenants; anteponer tenant_id volvería el índice inútil para esa consulta.
-- Es un índice parcial y minúsculo — solo los lotes vivos —, no un barrido de tabla.
CREATE INDEX IF NOT EXISTS ix_standalone_document_batches_pendientes
  ON admin.standalone_document_batches (created_at)
  WHERE status IN ('queued', 'processing');

COMMENT ON TABLE admin.standalone_document_batches IS
  'Cabecera de lote XLSX de generación documental (Feature #12201, I3). El XLSX fuente vive en storage; aquí metadata, hash y contadores. Sin tabla de items: cada fila del XLSX es una fila de admin.standalone_documents.';
COMMENT ON COLUMN admin.standalone_document_batches.source_filename IS
  '@pii:low — nombre del XLSX cargado; puede llevar razón social.';
COMMENT ON COLUMN admin.standalone_document_batches.source_storage_path IS
  '@pii:medium — ruta del XLSX fuente, que SÍ contiene los datos completos de las partes. No exponer en listados.';
COMMENT ON COLUMN admin.standalone_document_batches.claimed_at IS
  'R5 — marca de toma por el worker; base del reaper de lotes atascados.';

-- RLS decorativa, igual que en el DDL 105: no hay FORCE ROW LEVEL SECURITY y la app conecta como
-- owner (que bypassa la policy). El aislamiento efectivo es el filtro del repositorio + el
-- ownership check del endpoint. NO usarla como control ni apoyar tests en ella.
ALTER TABLE admin.standalone_document_batches ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.standalone_document_batches;
CREATE POLICY tenant_isolation ON admin.standalone_document_batches
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_standalone_document_batches_row_version ON admin.standalone_document_batches;
CREATE TRIGGER tr_standalone_document_batches_row_version BEFORE UPDATE ON admin.standalone_document_batches
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_standalone_document_batches_audit ON admin.standalone_document_batches;
CREATE TRIGGER tr_standalone_document_batches_audit AFTER INSERT OR UPDATE OR DELETE ON admin.standalone_document_batches
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ════════════════════════════════════════════════════════════════════════════════════════════════
-- Ampliación de admin.standalone_documents — la fila del lote ES el documento
-- ════════════════════════════════════════════════════════════════════════════════════════════════
ALTER TABLE admin.standalone_documents
  ADD COLUMN IF NOT EXISTS batch_id uuid,
  ADD COLUMN IF NOT EXISTS row_number integer,
  ADD COLUMN IF NOT EXISTS validation_errors jsonb NOT NULL DEFAULT '[]'::jsonb;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_standalone_documents_batch') THEN
    ALTER TABLE admin.standalone_documents
      ADD CONSTRAINT fk_standalone_documents_batch
      FOREIGN KEY (batch_id) REFERENCES admin.standalone_document_batches(id)
      ON DELETE CASCADE ON UPDATE CASCADE;
  END IF;

  -- batch_id y row_number viajan juntos: una fila de lote sin número de fila no es rastreable
  -- hasta el XLSX, y un número de fila sin lote no significa nada.
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_standalone_documents_batch_row') THEN
    ALTER TABLE admin.standalone_documents
      ADD CONSTRAINT ck_standalone_documents_batch_row
      CHECK (
        (batch_id IS NULL AND row_number IS NULL)
        OR (batch_id IS NOT NULL AND row_number IS NOT NULL AND row_number > 0)
      );
  END IF;
END $$;

-- Una fila por número de fila del XLSX: hace idempotente el reproceso del lote (R5).
CREATE UNIQUE INDEX IF NOT EXISTS uq_standalone_documents_batch_row
  ON admin.standalone_documents (batch_id, row_number)
  WHERE batch_id IS NOT NULL;

-- Filtro «por lote» del historial (CF-18) y armado del ZIP «solo generados» (CF-15). Cubre además
-- la FK batch_id (A9).
CREATE INDEX IF NOT EXISTS ix_standalone_documents_batch_status
  ON admin.standalone_documents (batch_id, status)
  WHERE batch_id IS NOT NULL;

COMMENT ON COLUMN admin.standalone_documents.batch_id IS
  'Lote de origen (admin.standalone_document_batches). NULL = generación individual. Con él, row_number es obligatorio.';
COMMENT ON COLUMN admin.standalone_documents.row_number IS
  'Número de fila en el XLSX (1-based, sin contar el encabezado). Permite señalar la fila exacta al usuario (CF-13).';
COMMENT ON COLUMN admin.standalone_documents.validation_errors IS
  '@pii:none — lista [{code, field, message}] de la fila del XLSX (CF-13). Sin valores capturados.';

-- ────────────────────────────────────────────────────────────────────────────────────────────────
-- Se reemplaza la función de inmutabilidad del DDL 105 para incluir el vínculo con el lote en la
-- capa 2 (identidad de la fila): batch_id y row_number no pueden reasignarse una vez creada la
-- fila, o la traza hasta el XLSX deja de ser confiable. El resto de la lógica es idéntica —
-- incluida la lista blanca que mantiene vivo CF-19 (downloaded_at / download_count).
-- El Down de la migración restaura textualmente la versión del DDL 105.
-- ────────────────────────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION admin.trg_standalone_document_immutable() RETURNS trigger AS $$
BEGIN
  -- Capa 1 — snapshots congelados una vez escritos.
  IF OLD.document_snapshot IS NOT NULL
     AND NEW.document_snapshot IS DISTINCT FROM OLD.document_snapshot THEN
    RAISE EXCEPTION 'admin.standalone_documents: document_snapshot es inmutable una vez escrito (id=%)', OLD.id
      USING ERRCODE = 'check_violation';
  END IF;

  IF OLD.rues_snapshot IS NOT NULL
     AND NEW.rues_snapshot IS DISTINCT FROM OLD.rues_snapshot THEN
    RAISE EXCEPTION 'admin.standalone_documents: rues_snapshot es inmutable una vez escrito (id=%)', OLD.id
      USING ERRCODE = 'check_violation';
  END IF;

  -- Capa 2 — identidad de la fila (incluye el vínculo con el lote desde el DDL 106).
  IF NEW.tenant_id             IS DISTINCT FROM OLD.tenant_id
     OR NEW.created_by_user_id IS DISTINCT FROM OLD.created_by_user_id
     OR NEW.document_type      IS DISTINCT FROM OLD.document_type
     OR NEW.created_at         IS DISTINCT FROM OLD.created_at
     OR NEW.batch_id           IS DISTINCT FROM OLD.batch_id
     OR NEW.row_number         IS DISTINCT FROM OLD.row_number THEN
    RAISE EXCEPTION 'admin.standalone_documents: tenant, autor, tipo, fecha de creación y vínculo con el lote son inmutables (id=%)', OLD.id
      USING ERRCODE = 'check_violation';
  END IF;

  -- Capa 3 — documento ya emitido: solo se admite la auditoría de descarga y el borrado lógico.
  IF OLD.status = 'generated' THEN
    IF NEW.status            IS DISTINCT FROM OLD.status
       OR NEW.storage_path   IS DISTINCT FROM OLD.storage_path
       OR NEW.storage_sha256 IS DISTINCT FROM OLD.storage_sha256
       OR NEW.size_bytes     IS DISTINCT FROM OLD.size_bytes
       OR NEW.filename       IS DISTINCT FROM OLD.filename
       OR NEW.input_summary  IS DISTINCT FROM OLD.input_summary
       OR NEW.scenario       IS DISTINCT FROM OLD.scenario THEN
      RAISE EXCEPTION 'admin.standalone_documents: estado, binario y resumen son inmutables una vez generated (id=%). Solo se admiten downloaded_at, download_count y el borrado lógico.', OLD.id
        USING ERRCODE = 'check_violation';
    END IF;
  END IF;

  RETURN NEW;
END; $$ LANGUAGE plpgsql;
