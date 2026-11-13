-- =============================================================================
-- Mejora ICT/QA (sin HU) — la firma de baúl se COPIA al trámite al radicarlo.
-- Migración: 20261111120000_FirmaBaulCopiaAlRadicar.
--
-- Antes la firma del baúl se resolvía EN VIVO en cada generación del FUR (y en el gate y el listado): un
-- trámite radicado podía perder la firma si el baúl del representante vencía o se desactivaba en medio
-- (el FUR se regenera al aprobar el OT y al asignar placa). Ahora, al salir de borrador (radicación), se
-- guarda una copia por parte/firmante y, desde entonces, FUR, regeneraciones, gate de identidad y listado
-- usan SOLO la copia. Los borradores siguen leyendo el baúl en vivo.
--
--   tramites.procedure_instances.firma_baul_copiada_at   marca «ya se copió» (aunque no hubiera ninguna firma
--                                                        de baúl: a partir de ahí el baúl en vivo no cuenta).
--                                                        NULL = borrador o trámite anterior a esta mejora
--                                                        (sigue en vivo, comportamiento previo).
--   tramites.procedure_instance_signature_vault_copies   una fila por firma de baúl copiada (por slot/parte).
--
-- La imagen se COPIA a un archivo propio del trámite (storage_path): la referencia al archivo del baúl
-- (source_storage_path) es solo trazabilidad y puede desaparecer sin afectar al trámite. signature_vault_id
-- NO lleva FK a propósito: la copia debe sobrevivir al borrado/reemplazo de la fila del baúl.
--
-- RLS decorativa (convención vigente): política tenant_isolation, sin FORCE. Idempotente, sin BEGIN/COMMIT.
-- =============================================================================

ALTER TABLE tramites.procedure_instances
    ADD COLUMN IF NOT EXISTS firma_baul_copiada_at timestamptz NULL;

COMMENT ON COLUMN tramites.procedure_instances.firma_baul_copiada_at IS
    'Momento en que, al radicar, se copió al trámite la firma de baúl de cada parte (tabla procedure_instance_signature_vault_copies). No NULL = FUR, gate y listado usan la copia y nunca el baúl en vivo. NULL = borrador o trámite anterior a la mejora (baúl en vivo).';

CREATE TABLE IF NOT EXISTS tramites.procedure_instance_signature_vault_copies (
    id                      uuid         NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_signature_vault_copies PRIMARY KEY (id),

    tenant_id               uuid         NOT NULL
        CONSTRAINT fk_procedure_instance_signature_vault_copies_tenant
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE CASCADE,

    procedure_instance_id   uuid         NOT NULL
        CONSTRAINT fk_procedure_instance_signature_vault_copies_instance
        REFERENCES tramites.procedure_instances (id) ON DELETE CASCADE ON UPDATE CASCADE,

    -- Slot de firma del documento (FurOverlayPartyKey: 'comprador', 'vendedor', 'comprador_2'…) y su rol.
    slot_key                varchar(40)  NOT NULL,
    party_role              varchar(20)  NOT NULL,

    -- Sujeto de identidad al que pertenece la firma (representante legal del actor jurídico). Es la llave con la
    -- que los consumidores resuelven la copia, en lugar de consultar el baúl.
    document_type           varchar(10)  NOT NULL,
    document_number         varchar(40)  NOT NULL,

    -- Datos del baúl al momento de la captura.
    signature_vault_id      uuid         NOT NULL,
    full_name               varchar(200) NOT NULL,
    signature_hash          varchar(200) NOT NULL DEFAULT '',
    codigo_hash             varchar(100) NULL,
    vigencia_desde          date         NOT NULL,
    vigencia_hasta          date         NOT NULL,
    captured_at             timestamptz  NOT NULL,

    -- Archivo de la firma: copia propia del trámite + hash de su contenido; el original es solo traza.
    storage_path            text         NOT NULL,
    storage_sha256          varchar(64)  NOT NULL,
    source_storage_path     text         NULL,

    row_version             bigint       NOT NULL DEFAULT 0,
    created_at              timestamptz  NOT NULL DEFAULT now(),
    created_by              uuid         NULL,
    updated_at              timestamptz  NULL,
    updated_by              uuid         NULL,
    deleted_at              timestamptz  NULL,
    deleted_by              uuid         NULL,

    CONSTRAINT ck_procedure_instance_signature_vault_copies_vigencia CHECK (vigencia_hasta >= vigencia_desde)
);

-- Una copia activa por slot del trámite.
CREATE UNIQUE INDEX IF NOT EXISTS uq_procedure_instance_signature_vault_copies_instance_slot
    ON tramites.procedure_instance_signature_vault_copies (tenant_id, procedure_instance_id, slot_key)
    WHERE deleted_at IS NULL;

-- Cubre la FK a procedure_instances (CASCADE) y la lectura por lote del listado.
CREATE INDEX IF NOT EXISTS ix_procedure_instance_signature_vault_copies_instance
    ON tramites.procedure_instance_signature_vault_copies (tenant_id, procedure_instance_id)
    WHERE deleted_at IS NULL;

COMMENT ON TABLE tramites.procedure_instance_signature_vault_copies IS
    'Copia, tomada al radicar, de la firma de baúl usada por cada parte/firmante del trámite (mejora ICT/QA sin HU). Con ella FUR, regeneraciones, gate de identidad y listado no consultan el baúl en vivo. signature_vault_id sin FK a propósito (la copia sobrevive al baúl).';
COMMENT ON COLUMN tramites.procedure_instance_signature_vault_copies.slot_key IS
    'Slot de firma del documento (comprador, vendedor, comprador_2, …): misma llave que FurOverlayPartyKey.';
COMMENT ON COLUMN tramites.procedure_instance_signature_vault_copies.document_number IS
    '@pii:medium — documento del representante legal dueño de la firma. No loguear.';
COMMENT ON COLUMN tramites.procedure_instance_signature_vault_copies.full_name IS
    '@pii:medium — nombre del titular de la firma en el baúl al momento de la captura.';
COMMENT ON COLUMN tramites.procedure_instance_signature_vault_copies.signature_vault_id IS
    'Id de la fila del baúl de la que se copió. Sin FK: la copia sobrevive a su borrado o reemplazo.';
COMMENT ON COLUMN tramites.procedure_instance_signature_vault_copies.storage_path IS
    'Archivo (imagen) de la firma COPIADO al trámite: con él se vuelve a estampar sin consultar el baúl.';
COMMENT ON COLUMN tramites.procedure_instance_signature_vault_copies.storage_sha256 IS
    'SHA-256 del contenido del archivo copiado.';
COMMENT ON COLUMN tramites.procedure_instance_signature_vault_copies.source_storage_path IS
    'Ruta del archivo en el baúl al capturar. Solo traza: puede dejar de existir.';
COMMENT ON COLUMN tramites.procedure_instance_signature_vault_copies.vigencia_desde IS
    'Vigencia de la firma del baúl al capturar (inicio). La copia no vence: conserva la vigencia que tenía al radicar.';

ALTER TABLE tramites.procedure_instance_signature_vault_copies ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.procedure_instance_signature_vault_copies;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_signature_vault_copies
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_procedure_instance_signature_vault_copies_row_version
    ON tramites.procedure_instance_signature_vault_copies;
CREATE TRIGGER tr_procedure_instance_signature_vault_copies_row_version
    BEFORE UPDATE ON tramites.procedure_instance_signature_vault_copies
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_procedure_instance_signature_vault_copies_audit
    ON tramites.procedure_instance_signature_vault_copies;
CREATE TRIGGER tr_procedure_instance_signature_vault_copies_audit
    AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_signature_vault_copies
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
