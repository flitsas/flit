-- Feature #12201 — Generación documental autónoma (ADR-0056-generacion-documental-standalone,
-- estado Propuesto). Incrementos I1 (Certificado RUES + historial + descarga) e I2 (Transferencia
-- de dominio A/B/C). Documentos emitidos SIN ProcedureInstance (CF-02): ninguna FK hacia tramites.*.
--
-- El PDF vive en storage (S3 / file-manager). Aquí solo metadata, integridad (sha256), snapshot
-- reproducible y auditoría de descarga. PROHIBIDO BYTEA (contraste deliberado con
-- admin.impronta_generations, que sí lo usa y NO se replica).
--
-- Una fila = un intento de generación, individual o de lote (sub-decisión §3.c del diseño). Las
-- columnas de lote (batch_id, row_number, validation_errors) las agrega el DDL 106 en I3.
--
-- DDL IDEMPOTENTE (CREATE ... IF NOT EXISTS + guardas DO $$ para constraints/policies/triggers) y
-- SIN BEGIN/COMMIT: chocaría con la transacción de la migración EF que lo ejecuta.

-- ════════════════════════════════════════════════════════════════════════════════════════════════
-- admin.standalone_documents
-- ════════════════════════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS admin.standalone_documents (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_standalone_documents PRIMARY KEY (id),

    tenant_id uuid NOT NULL
        CONSTRAINT fk_standalone_documents_tenant
        REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    -- Autor de la generación. Nombre alineado con tramites.procedure_instances.created_by_user_id
    -- (única convención con precedente en el repo para «el usuario que originó la fila»); es una
    -- columna de negocio con FK, distinta de la columna estándar de auditoría created_by.
    created_by_user_id uuid NOT NULL
        CONSTRAINT fk_standalone_documents_user
        REFERENCES identity.users(id) ON DELETE RESTRICT ON UPDATE CASCADE,

    -- 'transferencia_dominio_generada' NO es 'transferencia_dominio': este último ya existe como
    -- tipo ADJUNTABLE del catálogo documental y designa otra cosa (decisión del PO).
    document_type varchar(40) NOT NULL
        CONSTRAINT ck_standalone_documents_document_type
        CHECK (document_type IN ('certificado_rues', 'transferencia_dominio_generada')),

    -- Escenario normativo A/B/C (docs/plantilla-transferencia-dominio.md §3). Obligatorio en
    -- transferencia, prohibido en RUES: la coherencia se impone aquí, no solo en el handler.
    scenario char(1)
        CONSTRAINT ck_standalone_documents_scenario
        CHECK (scenario IS NULL OR scenario IN ('A', 'B', 'C')),

    -- Contrato interno de 4 estados (CF-21). La UI colapsa pending+processing en «En proceso».
    status varchar(20) NOT NULL DEFAULT 'pending'
        CONSTRAINT ck_standalone_documents_status
        CHECK (status IN ('pending', 'processing', 'generated', 'error')),

    error_code varchar(60),
    error_field varchar(120),

    storage_path varchar(1000),
    storage_sha256 varchar(64),
    size_bytes bigint
        CONSTRAINT ck_standalone_documents_size_bytes
        CHECK (size_bytes IS NULL OR size_bytes > 0),
    filename varchar(500),

    idempotency_key varchar(120),

    -- Resumen POBRE EN PII para el listado del historial (Ley 1581): placa, NIT, escenario, título
    -- jurídico y la declaración de régimen de CF-24. NUNCA domicilios completos, correos ni
    -- nombres completos. La restricción es de la capa de aplicación; el schema no puede imponerla.
    input_summary jsonb NOT NULL DEFAULT '{}'::jsonb,

    -- Snapshot congelado de la consulta RUES (patrón ADR-0037: {queriedAt, fields}).
    rues_snapshot jsonb,

    -- CF-26: payload COMPLETO e inmutable que alimentó al generador de transferencia. Se escribe
    -- una sola vez; es la evidencia reproducible del documento. Simétrico a rues_snapshot.
    document_snapshot jsonb,

    -- CF-19: auditoría de descarga. El contador NO limita descargas; se incrementa en CADA una,
    -- junto con downloaded_at (que guarda la ÚLTIMA descarga, no la primera).
    downloaded_at timestamptz,
    download_count integer NOT NULL DEFAULT 0
        CONSTRAINT ck_standalone_documents_download_count
        CHECK (download_count >= 0),

    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    deleted_by uuid,

    CONSTRAINT ck_standalone_documents_scenario_por_tipo CHECK (
        (document_type = 'transferencia_dominio_generada' AND scenario IS NOT NULL)
        OR (document_type = 'certificado_rues' AND scenario IS NULL)
    ),

    -- Un documento 'generated' TIENE binario. Sin esto, un bug deja filas descargables sin archivo.
    CONSTRAINT ck_standalone_documents_generated_completo CHECK (
        status <> 'generated'
        OR (storage_path IS NOT NULL AND storage_sha256 IS NOT NULL AND filename IS NOT NULL)
    ),

    -- 'error' sin código es un estado inauditable: obligaría a mirar logs para saber qué pasó.
    CONSTRAINT ck_standalone_documents_error_con_codigo CHECK (
        status <> 'error' OR error_code IS NOT NULL
    ),

    -- Nunca se descargó <=> contador en cero. Impide que la auditoría de CF-19 se desincronice.
    CONSTRAINT ck_standalone_documents_download_coherente CHECK (
        (downloaded_at IS NULL AND download_count = 0)
        OR (downloaded_at IS NOT NULL AND download_count > 0)
    )
);

-- Listado del historial: siempre por tenant y fecha desc (CF-17/CF-18). Parcial por deleted_at para
-- que coincida exactamente con el filtro del repositorio (tenant + no borrado).
CREATE INDEX IF NOT EXISTS ix_standalone_documents_tenant_created
  ON admin.standalone_documents (tenant_id, created_at DESC)
  WHERE deleted_at IS NULL;

-- Filtro por tipo dentro del tenant.
CREATE INDEX IF NOT EXISTS ix_standalone_documents_tenant_type_created
  ON admin.standalone_documents (tenant_id, document_type, created_at DESC)
  WHERE deleted_at IS NULL;

-- A9: la FK a identity.users necesita índice propio (ni el listado ni la PK la cubren).
CREATE INDEX IF NOT EXISTS ix_standalone_documents_tenant_user
  ON admin.standalone_documents (tenant_id, created_by_user_id);

-- Idempotencia (CF-16): ÚNICO PARCIAL — solo aplica a las filas que declaran clave. Excluye a
-- propósito las borradas lógicamente: si una fila soft-deleted retuviera la clave, el repositorio
-- (que filtra deleted_at IS NULL) no la encontraría al resolver el replay y el INSERT reventaría
-- con violación de unicidad en vez de devolver el documento existente.
CREATE UNIQUE INDEX IF NOT EXISTS uq_standalone_documents_tenant_idempotency
  ON admin.standalone_documents (tenant_id, idempotency_key)
  WHERE idempotency_key IS NOT NULL AND deleted_at IS NULL;

COMMENT ON TABLE admin.standalone_documents IS
  'Documentos generados SIN trámite (Feature #12201, ADR-0056-generacion-documental-standalone). PDF en storage; aquí metadata + hash + snapshot + auditoría de descarga.';
COMMENT ON COLUMN admin.standalone_documents.input_summary IS
  '@pii:low — resumen para el listado. Placa/NIT/escenario/declaración CF-24. Sin domicilios, correos ni nombres completos.';
COMMENT ON COLUMN admin.standalone_documents.rues_snapshot IS
  '@pii:medium — datos mercantiles congelados de la consulta RUES. Inmutable una vez escrito.';
COMMENT ON COLUMN admin.standalone_documents.document_snapshot IS
  '@pii:high — payload completo usado para renderizar (partes, documentos de identidad, domicilios). Inmutable una vez escrito. No exponer en listados ni en logs.';
COMMENT ON COLUMN admin.standalone_documents.filename IS
  '@pii:low — nombre del archivo; puede llevar razón social o placa.';
COMMENT ON COLUMN admin.standalone_documents.download_count IS
  'CF-19 — se incrementa en CADA descarga junto con downloaded_at. No es un cupo: no limita descargas.';

-- ────────────────────────────────────────────────────────────────────────────────────────────────
-- RLS: DEFENSA EN PROFUNDIDAD NOMINAL, NO ES UN CONTROL.
--
-- LEER ANTES DE APOYARSE EN ESTA POLÍTICA: en este repositorio NINGÚN DDL declara
-- FORCE ROW LEVEL SECURITY y la aplicación conecta como OWNER de las tablas — y el owner BYPASSA
-- las policies. Por lo tanto esta política NO aísla nada hoy.
--
-- El aislamiento EFECTIVO por tenant lo dan (a) el filtro WHERE tenant_id del repositorio y (b) el
-- ownership check del endpoint de descarga. Se escribe la policy por coherencia con las demás
-- tablas y para que valga si algún día se endurece el rol de conexión. PROHIBIDO que un test de
-- aislamiento se apoye en ella: pasaría igual sin RLS y daría falsa seguridad (§5.4 del diseño).
-- ────────────────────────────────────────────────────────────────────────────────────────────────
ALTER TABLE admin.standalone_documents ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON admin.standalone_documents;
CREATE POLICY tenant_isolation ON admin.standalone_documents
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_standalone_documents_row_version ON admin.standalone_documents;
CREATE TRIGGER tr_standalone_documents_row_version BEFORE UPDATE ON admin.standalone_documents
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

-- OJO (nota para security-agent): public.trg_audit_log copia la fila COMPLETA a audit.audit_logs,
-- así que document_snapshot (@pii:high) se duplica ahí en cada UPDATE, incluidas las descargas. Se
-- mantiene por A16 del checklist; recortar el alcance de esta traza exigiría ADR propio.
DROP TRIGGER IF EXISTS tr_standalone_documents_audit ON admin.standalone_documents;
CREATE TRIGGER tr_standalone_documents_audit AFTER INSERT OR UPDATE OR DELETE ON admin.standalone_documents
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- ────────────────────────────────────────────────────────────────────────────────────────────────
-- Inmutabilidad del snapshot y del binario — BEFORE UPDATE CON LISTA BLANCA (CF-05, CF-19, CF-26).
--
-- NO es el patrón de tramites.trg_field_value_immutable
-- (Ddl/06-HU10150-procedure-instances.sql:105-135). Aquel cubre INSERT y TODO UPDATE; copiarlo
-- literal dejaría CF-19 INSERVIBLE, porque downloaded_at y download_count tienen que poder
-- actualizarse en cada descarga.
--
-- Tres capas, todas BEFORE UPDATE:
--   1) Snapshots: una vez escritos (IS NOT NULL) no se pueden modificar ni borrar, en cualquier
--      estado. Son la evidencia reproducible del documento.
--   2) Identidad de la fila: tenant_id, created_by_user_id, document_type y created_at nunca mutan.
--   3) Ciclo de vida cerrado: con OLD.status = 'generated' quedan congelados también storage_path,
--      storage_sha256, size_bytes, filename, input_summary, scenario y status.
--
-- LISTA BLANCA (mutable durante el ciclo de vida): status, storage_path, storage_sha256,
-- size_bytes, filename, error_code, error_field, downloaded_at, download_count, row_version,
-- updated_at, updated_by, deleted_at, deleted_by — sujetos a la capa 3 una vez 'generated'.
-- La transición pending -> processing -> generated|error escribe libremente; el reproceso de una
-- fila en 'error' (R5) también, porque la capa 3 solo dispara desde 'generated'.
--
-- Orden de disparo: PostgreSQL ejecuta los BEFORE por nombre alfabético, así que
-- tr_standalone_documents_immutable corre ANTES de tr_standalone_documents_row_version. Por eso
-- row_version está exento: aún no fue incrementado y compararlo daría falsos positivos.
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

  -- Capa 2 — identidad de la fila.
  IF NEW.tenant_id             IS DISTINCT FROM OLD.tenant_id
     OR NEW.created_by_user_id IS DISTINCT FROM OLD.created_by_user_id
     OR NEW.document_type      IS DISTINCT FROM OLD.document_type
     OR NEW.created_at         IS DISTINCT FROM OLD.created_at THEN
    RAISE EXCEPTION 'admin.standalone_documents: tenant, autor, tipo y fecha de creación son inmutables (id=%)', OLD.id
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

DROP TRIGGER IF EXISTS tr_standalone_documents_immutable ON admin.standalone_documents;
CREATE TRIGGER tr_standalone_documents_immutable
  BEFORE UPDATE ON admin.standalone_documents
  FOR EACH ROW EXECUTE FUNCTION admin.trg_standalone_document_immutable();
