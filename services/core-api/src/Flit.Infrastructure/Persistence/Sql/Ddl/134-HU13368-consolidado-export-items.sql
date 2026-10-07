-- HU #13368 (Feature #13306, épica #13216) — motor de lotes de descarga masiva de consolidados: partes, ítems y
-- auditoría append-only. Migración: HU13368_ConsolidadoExportItems · ADR-0070 (Propuesto, adendas v3–v5) ·
-- 07-schema-13306 §3 + delta de cancelación de 09-diseno-13307 §7 (#13307).
--
-- Tres tablas en tramites (settings y batches las creó HU #13367, DDL 133):
--   consolidado_export_batch_parts  partes ZIP cifradas (tenant derivado del lote por trigger). Antes que items:
--                                   items.(batch_id, part_number) la referencia.
--   consolidado_export_batch_items  un trámite por fila (tenant DEL TRÁMITE, CF-15)
--   consolidado_export_audit        auditoría append-only Ley 1581 (sin FK, sin soft delete; patrón DDL 113)
--
-- RLS DECORATIVA (convención vigente): sin FORCE ROW LEVEL SECURITY y la app conecta como owner. El
-- aislamiento real es de aplicación: consultas del dueño por requested_by_user_id = sub; reclamo y
-- purga entre tenants por SQL parametrizado en el repositorio (sin IgnoreQueryFilters).
--
-- EXCEPCIONES (ADR-0070, adenda v3):
--   E2 audit append-only sin FK, sin soft delete, sin trg_audit_log ni row_version por trigger (A3.2, DDL 113).
--   E3 items y parts sin trg_audit_log ni deleted_* (ciclo de vida del lote; traza Ley 1581 = audit) (A3.2).
--   E4 índices sin tenant_id a la cabeza: reclamo, asignación, FK de trámite, carril de partes (A3.3).
--   E5 parts.tenant_id / audit.actor_tenant_id NULL solo en origen superadmin (A3.1, Q8).
-- Delta de cancelación (adenda v4, #13307 D7): parte 'descartada', ítem 'cancelado', lote_cancelado con conteos.
--
-- Idempotente y sin BEGIN/COMMIT: lo ejecuta la migración EF dentro de su transacción.

-- ════════════════════════════════════════════════════════════════════════════════════════════════
-- 1. tramites.consolidado_export_batch_parts — partes ZIP cifradas
-- ════════════════════════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS tramites.consolidado_export_batch_parts (
    id                 uuid        NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_consolidado_export_batch_parts PRIMARY KEY (id),
    tenant_id          uuid        NULL,
    batch_id           uuid        NOT NULL,
    part_number        smallint    NOT NULL
        CONSTRAINT ck_consolidado_export_batch_parts_part_number CHECK (part_number >= 1),
    status             text        NOT NULL DEFAULT 'pendiente'
        -- descartada (#13307): parte sin cerrar al cancelar el lote. No exige binario y no arrastra el lote a fallido.
        CONSTRAINT ck_consolidado_export_batch_parts_status CHECK (status IN (
            'pendiente', 'empaquetando', 'cerrada', 'fallida', 'purgada', 'descartada')),
    attempts           smallint    NOT NULL DEFAULT 0,
    lease_until        timestamptz NULL,
    pdf_count          integer     NOT NULL DEFAULT 0,
    omitted_count      integer     NOT NULL DEFAULT 0,
    plain_size_bytes   bigint      NULL,
    stored_size_bytes  bigint      NULL,
    stored_sha256      text        NULL,
    storage_path       text        NULL,
    nonce_prefix       bytea       NULL,
    closed_at          timestamptz NULL,
    purged_at          timestamptz NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    created_by         uuid        NULL,
    updated_at         timestamptz NULL,
    updated_by         uuid        NULL,
    row_version        bigint      NOT NULL DEFAULT 0,

    CONSTRAINT fk_consolidado_export_batch_parts_batches FOREIGN KEY (batch_id)
        REFERENCES tramites.consolidado_export_batches (id) ON DELETE CASCADE ON UPDATE RESTRICT,
    CONSTRAINT fk_consolidado_export_batch_parts_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT uq_consolidado_export_batch_parts_batch_number UNIQUE (batch_id, part_number),

    CONSTRAINT ck_consolidado_export_batch_parts_counts CHECK (
        attempts >= 0 AND pdf_count >= 0 AND omitted_count >= 0
        AND (plain_size_bytes IS NULL OR plain_size_bytes >= 0)
        AND (stored_size_bytes IS NULL OR stored_size_bytes >= 0)),
    CONSTRAINT ck_consolidado_export_batch_parts_sha256 CHECK (stored_sha256 IS NULL OR stored_sha256 ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_consolidado_export_batch_parts_lease CHECK (status <> 'empaquetando' OR lease_until IS NOT NULL),
    CONSTRAINT ck_consolidado_export_batch_parts_closed CHECK (
        status NOT IN ('cerrada', 'purgada')
        OR (storage_path IS NOT NULL AND stored_sha256 IS NOT NULL AND stored_size_bytes IS NOT NULL
            AND plain_size_bytes IS NOT NULL AND nonce_prefix IS NOT NULL AND closed_at IS NOT NULL)),
    CONSTRAINT ck_consolidado_export_batch_parts_purged CHECK ((status = 'purgada') = (purged_at IS NOT NULL))
);

-- uq_consolidado_export_batch_parts_batch_number cubre la FK a batches (A9).
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batch_parts_tenant
    ON tramites.consolidado_export_batch_parts (tenant_id);
-- E4: carril de empaquetado entre tenants.
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batch_parts_claim
    ON tramites.consolidado_export_batch_parts (created_at)
    WHERE status IN ('pendiente', 'empaquetando');

-- E5: tenant_id de la parte = tenant_id del lote, siempre (incluido NULL en superadmin). Lo fija la base,
-- no la aplicación, para que una parte nunca quede imputada a otra compañía.
CREATE OR REPLACE FUNCTION tramites.trg_consolidado_export_batch_parts_tenant() RETURNS trigger AS $$
BEGIN
    SELECT b.tenant_id INTO NEW.tenant_id
      FROM tramites.consolidado_export_batches b
     WHERE b.id = NEW.batch_id;
    RETURN NEW;
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_consolidado_export_batch_parts_tenant ON tramites.consolidado_export_batch_parts;
CREATE TRIGGER tr_consolidado_export_batch_parts_tenant
    BEFORE INSERT OR UPDATE OF tenant_id, batch_id ON tramites.consolidado_export_batch_parts
    FOR EACH ROW EXECUTE FUNCTION tramites.trg_consolidado_export_batch_parts_tenant();

DROP TRIGGER IF EXISTS tr_consolidado_export_batch_parts_row_version ON tramites.consolidado_export_batch_parts;
CREATE TRIGGER tr_consolidado_export_batch_parts_row_version
    BEFORE UPDATE ON tramites.consolidado_export_batch_parts
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

ALTER TABLE tramites.consolidado_export_batch_parts ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.consolidado_export_batch_parts;
CREATE POLICY tenant_isolation ON tramites.consolidado_export_batch_parts
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

COMMENT ON TABLE tramites.consolidado_export_batch_parts IS
    'HU #13368 (Feature #13306, ADR-0070) — partes ZIP de un lote, cifradas con AES-256-GCM por bloques (clave del lote). Sin trg_audit_log ni soft delete (E3): viven y mueren con el lote; la traza Ley 1581 es consolidado_export_audit.';
COMMENT ON COLUMN tramites.consolidado_export_batch_parts.tenant_id IS 'Copia del tenant del lote, fijada por tr_consolidado_export_batch_parts_tenant. NULL en lotes de Super Admin (Q8).';
COMMENT ON COLUMN tramites.consolidado_export_batch_parts.status IS 'pendiente → empaquetando → cerrada | fallida; cerrada → purgada (retención). descartada (#13307): parte sin cerrar cuando se canceló el lote.';
COMMENT ON COLUMN tramites.consolidado_export_batch_parts.storage_path IS 'Id opaco en el file-manager del texto cifrado. No es una URL pública; sin la DEK del lote es ilegible.';
COMMENT ON COLUMN tramites.consolidado_export_batch_parts.stored_sha256 IS 'SHA-256 (hex minúsculas) del texto cifrado almacenado.';
COMMENT ON COLUMN tramites.consolidado_export_batch_parts.nonce_prefix IS 'Prefijo de nonce de la parte (framing AES-GCM por bloques; revisión del security-agent).';

-- ════════════════════════════════════════════════════════════════════════════════════════════════
-- 2. tramites.consolidado_export_batch_items — un trámite por fila
-- ════════════════════════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS tramites.consolidado_export_batch_items (
    id                     uuid        NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_consolidado_export_batch_items PRIMARY KEY (id),
    tenant_id              uuid        NOT NULL,
    batch_id               uuid        NOT NULL,
    procedure_instance_id  uuid        NOT NULL,
    position               integer     NOT NULL,
    status                 text        NOT NULL DEFAULT 'pendiente'
        -- cancelado (#13307): ítem vivo cuando se canceló el lote. Sin código, parte ni modo de entrega.
        CONSTRAINT ck_consolidado_export_batch_items_status CHECK (status IN (
            'pendiente', 'procesando', 'incluido', 'omitido', 'cancelado')),
    attempts               smallint    NOT NULL DEFAULT 0,
    next_attempt_at        timestamptz NOT NULL DEFAULT now(),
    lease_until            timestamptz NULL,
    claimed_by             text        NULL,
    reference_number       text        NOT NULL,
    plate                  text        NULL,
    attachment_id          uuid        NULL,
    storage_path           text        NULL,
    size_bytes             bigint      NULL,
    delivery_mode          text        NULL
        CONSTRAINT ck_consolidado_export_batch_items_delivery_mode CHECK (delivery_mode IN ('existente', 'generado')),
    omission_code          text        NULL
        -- Q12 / §3 v2: catálogo exacto, contrato con ConsolidadoLoteOmisiones (#13371), en su orden.
        -- fur_requerido aplica a consolidado y a consolidado_maestro (el CHECK no depende del tipo).
        -- Eliminados en v2: consolidado_no_generado, en_regeneracion.
        CONSTRAINT ck_consolidado_export_batch_items_omission_code CHECK (omission_code IN (
            'fur_requerido', 'migrado_solo_lectura',
            'sin_adjuntos', 'adjunto_no_disponible', 'mimetype_no_soportado',
            'organismo_requerido', 'modalidad_no_soportada',
            'quipux_solo_lectura', 'acceso_revocado', 'error_tecnico')),
    omission_reason        text        NULL,
    part_number            smallint    NULL,
    processed_at           timestamptz NULL,
    created_at             timestamptz NOT NULL DEFAULT now(),
    created_by             uuid        NOT NULL,
    updated_at             timestamptz NULL,
    updated_by             uuid        NULL,
    row_version            bigint      NOT NULL DEFAULT 0,

    CONSTRAINT fk_consolidado_export_batch_items_batches FOREIGN KEY (batch_id)
        REFERENCES tramites.consolidado_export_batches (id) ON DELETE CASCADE ON UPDATE RESTRICT,
    CONSTRAINT fk_consolidado_export_batch_items_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_consolidado_export_batch_items_procedure_instances FOREIGN KEY (procedure_instance_id)
        REFERENCES tramites.procedure_instances (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    -- NO ACTION (se verifica al final de la sentencia): el CASCADE desde batches borra items y parts
    -- en la misma sentencia sin que el orden importe.
    CONSTRAINT fk_consolidado_export_batch_items_batch_parts FOREIGN KEY (batch_id, part_number)
        REFERENCES tramites.consolidado_export_batch_parts (batch_id, part_number) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT uq_consolidado_export_batch_items_batch_instance UNIQUE (batch_id, procedure_instance_id),

    CONSTRAINT ck_consolidado_export_batch_items_numbers CHECK (
        position >= 0 AND attempts >= 0
        AND (part_number IS NULL OR part_number >= 1)
        AND (size_bytes IS NULL OR size_bytes >= 0)),
    CONSTRAINT ck_consolidado_export_batch_items_lease CHECK (status <> 'procesando' OR lease_until IS NOT NULL),
    -- Vivo (pendiente/procesando): sin resultado ni parte.
    CONSTRAINT ck_consolidado_export_batch_items_alive CHECK (
        status NOT IN ('pendiente', 'procesando')
        OR (part_number IS NULL AND delivery_mode IS NULL AND processed_at IS NULL)),
    -- Incluido: snapshot completo del PDF entregado + cómo se obtuvo (v2).
    CONSTRAINT ck_consolidado_export_batch_items_included CHECK (
        status <> 'incluido'
        OR (attachment_id IS NOT NULL AND storage_path IS NOT NULL AND size_bytes IS NOT NULL
            AND delivery_mode IS NOT NULL AND processed_at IS NOT NULL)),
    -- Omitido ⇔ código; con texto del CSV y fecha (también incluido → omitido adjunto_no_disponible, C6).
    CONSTRAINT ck_consolidado_export_batch_items_omitted CHECK (
        (status = 'omitido') = (omission_code IS NOT NULL)
        AND (status <> 'omitido' OR (omission_reason IS NOT NULL AND processed_at IS NOT NULL)))
);

-- uq_consolidado_export_batch_items_batch_instance cubre la FK a batches (A9).
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batch_items_tenant
    ON tramites.consolidado_export_batch_items (tenant_id);
-- E4: índice de FK (A9 prevalece sobre A11).
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batch_items_procedure_instance
    ON tramites.consolidado_export_batch_items (procedure_instance_id);
-- A9 de la FK compuesta a parts + lectura de los ítems de la parte k al empaquetar.
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batch_items_batch_part
    ON tramites.consolidado_export_batch_items (batch_id, part_number)
    WHERE part_number IS NOT NULL;
-- E4: reclamo dentro del lote (FOR UPDATE SKIP LOCKED).
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batch_items_claim
    ON tramites.consolidado_export_batch_items (batch_id, position)
    WHERE status IN ('pendiente', 'procesando');
-- E4: asignación avara de partes (ítems terminados sin parte, por processed_at).
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batch_items_unassigned
    ON tramites.consolidado_export_batch_items (batch_id, processed_at)
    WHERE status IN ('incluido', 'omitido') AND part_number IS NULL;

DROP TRIGGER IF EXISTS tr_consolidado_export_batch_items_row_version ON tramites.consolidado_export_batch_items;
CREATE TRIGGER tr_consolidado_export_batch_items_row_version
    BEFORE UPDATE ON tramites.consolidado_export_batch_items
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

ALTER TABLE tramites.consolidado_export_batch_items ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.consolidado_export_batch_items;
CREATE POLICY tenant_isolation ON tramites.consolidado_export_batch_items
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

COMMENT ON TABLE tramites.consolidado_export_batch_items IS
    'HU #13368 (Feature #13306, ADR-0070) — un trámite por fila dentro de un lote. Tabla operativa de alta rotación: sin trg_audit_log ni soft delete (E3); la traza Ley 1581 es consolidado_export_audit.';
COMMENT ON COLUMN tramites.consolidado_export_batch_items.tenant_id IS 'Compañía DEL TRÁMITE (contexto de proceso, CF-15), no la del solicitante. Siempre NOT NULL, también en lotes de Super Admin.';
COMMENT ON COLUMN tramites.consolidado_export_batch_items.status IS 'pendiente → procesando → incluido | omitido. cancelado (#13307): ítem vivo cuando se canceló el lote; el cierre en vuelo (WHERE status = procesando) actualiza 0 filas.';
COMMENT ON COLUMN tramites.consolidado_export_batch_items.plate IS '@pii:low — placa del vehículo (dato personal indirecto), copiada para nombrar el PDF ({radicado}_{PLACA}.pdf) y el omitidos.csv. La purga la conserva (decisión S2). No registrar en logs.';
COMMENT ON COLUMN tramites.consolidado_export_batch_items.reference_number IS 'Radicado del trámite (snapshot al crear el lote).';
COMMENT ON COLUMN tramites.consolidado_export_batch_items.storage_path IS 'Snapshot del id opaco del PDF entregado en el file-manager. Si al empaquetar no se lee, se toma el adjunto actual del tipo (C6).';
COMMENT ON COLUMN tramites.consolidado_export_batch_items.delivery_mode IS 'v2: existente = adjunto ya guardado, tomado tal cual (cualquier estado o vigencia); generado = primera generación por el lote. Obligatorio si status = incluido.';
COMMENT ON COLUMN tramites.consolidado_export_batch_items.omission_code IS 'Motivo de omisión (catálogo cerrado §3 v2 = ConsolidadoLoteOmisiones). Presente si y solo si status = omitido.';
COMMENT ON COLUMN tramites.consolidado_export_batch_items.omission_reason IS 'Texto legible del motivo para omitidos.csv (sin datos personales).';
COMMENT ON COLUMN tramites.consolidado_export_batch_items.claimed_by IS 'Instancia/slot que reclamó el ítem (diagnóstico).';

-- ════════════════════════════════════════════════════════════════════════════════════════════════
-- 3. tramites.consolidado_export_audit — auditoría append-only (patrón DDL 113)
-- ════════════════════════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS tramites.consolidado_export_audit (
    id                  uuid        NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_consolidado_export_audit PRIMARY KEY (id),
    occurred_at         timestamptz NOT NULL DEFAULT now(),
    event               text        NOT NULL
        CONSTRAINT ck_consolidado_export_audit_event CHECK (event IN (
            'lote_creado', 'lote_finalizado', 'parte_descargada', 'lote_cancelado', 'lote_purgado')),
    origin              text        NOT NULL
        CONSTRAINT ck_consolidado_export_audit_origin CHECK (origin IN ('tramites', 'superadmin', 'ot_bandeja')),
    batch_id            uuid        NOT NULL,
    actor_user_id       uuid        NOT NULL,
    actor_tenant_id     uuid        NULL,
    actor_role_code     text        NOT NULL,
    scope_tenant_id     uuid        NULL,
    reached_tenant_ids  uuid[]      NULL,
    document_type       text        NOT NULL
        CONSTRAINT ck_consolidado_export_audit_document_type CHECK (document_type IN ('consolidado', 'consolidado_maestro')),
    selection_mode      text        NULL
        CONSTRAINT ck_consolidado_export_audit_selection_mode CHECK (selection_mode IN ('ids', 'filtro')),
    filter_summary      jsonb       NULL,
    ids_count           integer     NULL,
    excluded_count      integer     NULL,
    total_items         integer     NULL,
    included_count      integer     NULL,
    omitted_count       integer     NULL,
    generated_count     integer     NULL,
    part_number         smallint    NULL,
    client_ip           inet        NULL,
    user_agent          text        NULL,
    row_version         bigint      NOT NULL DEFAULT 0,

    -- Q8: igual que en batches.
    CONSTRAINT ck_consolidado_export_audit_tenant_origin CHECK ((origin = 'superadmin') = (actor_tenant_id IS NULL)),
    -- lote_creado: modo, total y compañías alcanzadas; vacío solo si el lote nació sin ítems.
    CONSTRAINT ck_consolidado_export_audit_created CHECK (
        event <> 'lote_creado'
        OR (selection_mode IS NOT NULL AND total_items IS NOT NULL AND reached_tenant_ids IS NOT NULL
            AND (cardinality(reached_tenant_ids) > 0) = (total_items > 0))),
    CONSTRAINT ck_consolidado_export_audit_finished CHECK (
        event <> 'lote_finalizado'
        OR (total_items IS NOT NULL AND included_count IS NOT NULL AND omitted_count IS NOT NULL
            AND generated_count IS NOT NULL AND generated_count <= included_count)),
    -- #13307 D7: lote_cancelado con conteos (como lote_finalizado), para que la traza Ley 1581 diga cuánto se exportó.
    CONSTRAINT ck_consolidado_export_audit_cancelled CHECK (
        event <> 'lote_cancelado'
        OR (total_items IS NOT NULL AND included_count IS NOT NULL AND omitted_count IS NOT NULL
            AND generated_count IS NOT NULL AND generated_count <= included_count)),
    CONSTRAINT ck_consolidado_export_audit_part CHECK (event <> 'parte_descargada' OR part_number IS NOT NULL),
    CONSTRAINT ck_consolidado_export_audit_counts CHECK (
        (ids_count IS NULL OR ids_count >= 0) AND (excluded_count IS NULL OR excluded_count >= 0)
        AND (total_items IS NULL OR total_items >= 0) AND (included_count IS NULL OR included_count >= 0)
        AND (omitted_count IS NULL OR omitted_count >= 0) AND (generated_count IS NULL OR generated_count >= 0)
        AND (part_number IS NULL OR part_number >= 1))
);

CREATE INDEX IF NOT EXISTS ix_consolidado_export_audit_actor_tenant_occurred
    ON tramites.consolidado_export_audit (actor_tenant_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_consolidado_export_audit_batch
    ON tramites.consolidado_export_audit (batch_id);
-- Consulta del titular/compañía alcanzada (Super Admin multicompañía) y policy RLS.
CREATE INDEX IF NOT EXISTS ix_consolidado_export_audit_reached_tenant_ids
    ON tramites.consolidado_export_audit USING gin (reached_tenant_ids);

CREATE OR REPLACE FUNCTION tramites.trg_consolidado_export_audit_immutable() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'tramites.consolidado_export_audit es append-only: no se permite % (id=%)', TG_OP, OLD.id
        USING ERRCODE = 'check_violation';
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_consolidado_export_audit_immutable ON tramites.consolidado_export_audit;
CREATE TRIGGER tr_consolidado_export_audit_immutable
    BEFORE UPDATE OR DELETE ON tramites.consolidado_export_audit
    FOR EACH ROW EXECUTE FUNCTION tramites.trg_consolidado_export_audit_immutable();

ALTER TABLE tramites.consolidado_export_audit ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.consolidado_export_audit;
CREATE POLICY tenant_isolation ON tramites.consolidado_export_audit
    USING (
        NULLIF(current_setting('app.is_superadmin', true), '') = 'true'
        OR actor_tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
        OR NULLIF(current_setting('app.current_tenant_id', true), '')::uuid = ANY (reached_tenant_ids)
    );

COMMENT ON TABLE tramites.consolidado_export_audit IS
    'HU #13368 (Feature #13306, ADR-0070 D8) — auditoría append-only Ley 1581 de los lotes de descarga masiva: creación (en la misma transacción que el lote), cierre, descarga de parte (fail-closed antes del primer byte), cancelación y purga. Solo identificadores y conteos: nunca PDF, nombres, documentos ni placas. SIN FK (sobrevive al lote, al usuario y a la compañía; excepción A7/A8/A9 como DDL 113). UPDATE/DELETE rechazados por tr_consolidado_export_audit_immutable.';
COMMENT ON COLUMN tramites.consolidado_export_audit.actor_tenant_id IS 'Compañía del actor (identity.tenants.id, sin FK). NULL si y solo si origin = superadmin (Q8).';
COMMENT ON COLUMN tramites.consolidado_export_audit.reached_tenant_ids IS 'Compañías DISTINTAS de los trámites congelados en el lote (items.tenant_id). Obligatorio en lote_creado; vacío solo si total_items = 0. En un lote de Gestor es {su compañía}; en Super Admin, todas las alcanzadas (Q8). Se repite en lote_cancelado del Super Admin.';
COMMENT ON COLUMN tramites.consolidado_export_audit.filter_summary IS '@pii:low — filtro aplicado con las listas pegadas (placa/VIN/radicado) reducidas a {campo, operador, cantidad}. Nunca los valores de esas listas.';
COMMENT ON COLUMN tramites.consolidado_export_audit.client_ip IS '@pii:medium — IP del actor (evidencia de acceso, Ley 1581).';
COMMENT ON COLUMN tramites.consolidado_export_audit.user_agent IS '@pii:low — navegador del actor (evidencia de acceso).';
COMMENT ON COLUMN tramites.consolidado_export_audit.row_version IS 'Convención A5. Append-only: ninguna fila cambia de versión (sin trigger de row_version).';
