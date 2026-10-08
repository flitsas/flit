-- HU #13367 (Feature #13306, épica #13216) — motor de lotes de descarga masiva de consolidados: parámetros y lotes.
-- Migración: HU13367_ConsolidadoExportBatches · ADR-0070 (Propuesto, adendas v3 y v5) · 07-schema-13306 §3.
--
-- Dos tablas en tramites (partes, ítems y auditoría append-only las crea HU #13368 en el DDL siguiente):
--   consolidado_export_settings   parámetros del motor, fila única global (sin tenant_id, sin RLS)
--   consolidado_export_batches    cabecera del lote (tenant_id NULL solo en origen superadmin, Q8)
--
-- RLS DECORATIVA (convención vigente): sin FORCE ROW LEVEL SECURITY y la app conecta como owner. El
-- aislamiento real es de aplicación: consultas del dueño por requested_by_user_id = sub; reclamo y
-- purga entre tenants por SQL parametrizado en el repositorio (sin IgnoreQueryFilters).
--
-- EXCEPCIONES (ADR-0070, adenda v3):
--   E1 settings global sin tenant_id/RLS/soft delete (A3.4, precedente DDL 67).
--   E4 índices sin tenant_id a la cabeza: activo por usuario, por solicitante, reclamo, purga, FK (A3.3).
--   E5 batches.tenant_id NULL si y solo si origin = 'superadmin' (A3.1, Q8).
--   E6 batches sin trg_audit_log: dek_wrapped nunca se copia a audit.audit_logs (adenda v6, H1).
-- Delta R-d (adenda v5, A5.5): ot_transit_office_id existe si y solo si origin = 'ot_bandeja'.
-- Delta vista de red (HU #13417, adenda v7, A7.1): network_scope = lote 'tramites' de una cabeza creado desde la vista
-- de red; scope_tenant_id = hija acotada (nunca la propia compañía) o NULL = toda la red. Editado en sitio: la
-- migración HU13367_ConsolidadoExportBatches no se había aplicado en ningún entorno compartido.
--
-- Idempotente y sin BEGIN/COMMIT: lo ejecuta la migración EF dentro de su transacción.

-- ════════════════════════════════════════════════════════════════════════════════════════════════
-- 1. tramites.consolidado_export_settings — fila única global
-- ════════════════════════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS tramites.consolidado_export_settings (
    id                    uuid        NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_consolidado_export_settings PRIMARY KEY (id),
    max_pdfs_per_part     integer     NOT NULL DEFAULT 500
        CONSTRAINT ck_consolidado_export_settings_max_pdfs CHECK (max_pdfs_per_part BETWEEN 1 AND 5000),
    max_mb_per_part       integer     NOT NULL DEFAULT 250
        CONSTRAINT ck_consolidado_export_settings_max_mb CHECK (max_mb_per_part BETWEEN 10 AND 2048),
    item_slots            smallint    NOT NULL DEFAULT 2
        CONSTRAINT ck_consolidado_export_settings_item_slots CHECK (item_slots BETWEEN 1 AND 6),
    item_timeout_seconds  integer     NOT NULL DEFAULT 300
        CONSTRAINT ck_consolidado_export_settings_item_timeout CHECK (item_timeout_seconds BETWEEN 1 AND 3600),
    item_lease_seconds    integer     NOT NULL DEFAULT 600,
    max_item_attempts     smallint    NOT NULL DEFAULT 3
        CONSTRAINT ck_consolidado_export_settings_max_item_attempts CHECK (max_item_attempts BETWEEN 1 AND 10),
    retry_delay_seconds   integer     NOT NULL DEFAULT 30
        CONSTRAINT ck_consolidado_export_settings_retry_delay CHECK (retry_delay_seconds BETWEEN 5 AND 3600),
    part_timeout_seconds  integer     NOT NULL DEFAULT 1200
        CONSTRAINT ck_consolidado_export_settings_part_timeout CHECK (part_timeout_seconds BETWEEN 1 AND 7200),
    part_lease_seconds    integer     NOT NULL DEFAULT 1800,
    max_part_attempts     smallint    NOT NULL DEFAULT 3
        CONSTRAINT ck_consolidado_export_settings_max_part_attempts CHECK (max_part_attempts BETWEEN 1 AND 10),
    retention_hours       integer     NOT NULL DEFAULT 24
        CONSTRAINT ck_consolidado_export_settings_retention CHECK (retention_hours BETWEEN 1 AND 168),
    -- M1 (épica #13216): tope total de trámites por lote, todos los orígenes y modos, sobre la selección resuelta.
    -- Code review Obs1: máximo 32.766 = smallint(32.767) - 1. part_number es smallint y un lote puede tener UNA parte
    -- por ítem sea cual sea max_pdfs_per_part (un PDF mayor que max_mb_per_part va solo, y dos que juntos lo superan
    -- también), más como mucho la parte 0/0 de un lote sin partes; los omitidos tardíos van con al menos un ítem propio.
    -- Un CHECK cruzado con max_pdfs_per_part no basta: M, no N, es quien fija el peor caso.
    max_items_per_batch   integer     NOT NULL DEFAULT 10000
        CONSTRAINT ck_consolidado_export_settings_max_items CHECK (max_items_per_batch BETWEEN 1 AND 32766),
    is_active             boolean     NOT NULL DEFAULT true,
    created_at            timestamptz NOT NULL DEFAULT now(),
    created_by            uuid        NULL,
    updated_at            timestamptz NULL,
    updated_by            uuid        NULL,
    row_version           bigint      NOT NULL DEFAULT 0,
    -- Invariante de no-solapamiento (D2): una ejecución nunca sobrevive a su propio lease.
    -- Security L1: tiempos y leases con tope (horas, no int.MaxValue) para que un worker caído no deje ítems ni
    -- partes arrendados indefinidamente. Rangos inclusivos; paridad con ConsolidadoExportSettingsRangos.
    CONSTRAINT ck_consolidado_export_settings_item_lease CHECK (item_lease_seconds > item_timeout_seconds AND item_lease_seconds <= 7200),
    CONSTRAINT ck_consolidado_export_settings_part_lease CHECK (part_lease_seconds > part_timeout_seconds AND part_lease_seconds <= 14400)
);

-- Fila única: misma llave constante para todas las filas (patrón uq_notification_test_settings_singleton).
CREATE UNIQUE INDEX IF NOT EXISTS uq_consolidado_export_settings_singleton
    ON tramites.consolidado_export_settings ((true));

DROP TRIGGER IF EXISTS tr_consolidado_export_settings_row_version ON tramites.consolidado_export_settings;
CREATE TRIGGER tr_consolidado_export_settings_row_version
    BEFORE UPDATE ON tramites.consolidado_export_settings
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_consolidado_export_settings_audit ON tramites.consolidado_export_settings;
CREATE TRIGGER tr_consolidado_export_settings_audit
    AFTER INSERT OR UPDATE OR DELETE ON tramites.consolidado_export_settings
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- Sembrado reproducible: no duplica ni pisa valores ya calibrados.
INSERT INTO tramites.consolidado_export_settings (id)
SELECT uuidv7()
 WHERE NOT EXISTS (SELECT 1 FROM tramites.consolidado_export_settings);

COMMENT ON TABLE tramites.consolidado_export_settings IS
    'HU #13367 (Feature #13306, ADR-0070) — parámetros del motor de lotes de descarga masiva de consolidados. Fila única (uq_consolidado_export_settings_singleton). GLOBAL DE PLATAFORMA: sin tenant_id y, en consecuencia, sin RLS (como admin.notification_test_settings). Editable por el Super Admin (HU #13420); histórico en audit.audit_logs.';
COMMENT ON COLUMN tramites.consolidado_export_settings.max_pdfs_per_part IS 'N: PDF por parte ZIP. Recalibrar con la medición P-1 (N ≈ M / p50).';
COMMENT ON COLUMN tramites.consolidado_export_settings.max_mb_per_part IS 'M: MB de PDF en claro por parte. Lo limita la descarga como blob en el navegador.';
COMMENT ON COLUMN tramites.consolidado_export_settings.item_slots IS 'Ítems concurrentes del motor por instancia = tope de generaciones de PDF del lote (v2, antes k_total).';
COMMENT ON COLUMN tramites.consolidado_export_settings.item_lease_seconds IS 'Lease del reclamo de ítem; siempre > item_timeout_seconds (sin heartbeat) y <= 7200 s.';
COMMENT ON COLUMN tramites.consolidado_export_settings.retry_delay_seconds IS 'Espera antes de reintentar un ítem por error técnico (v2: 30 s, de 5 a 3600).';
COMMENT ON COLUMN tramites.consolidado_export_settings.retention_hours IS 'Horas que se conservan las partes tras terminar el lote (H6a: 24). Luego, borrado criptográfico.';
COMMENT ON COLUMN tramites.consolidado_export_settings.max_items_per_batch IS 'M1: tope total de trámites de un lote (todos los orígenes, modos ids y filtro), contado sobre la selección resuelta (exclusiones e intersección de seguridad aplicadas). Superarlo = 422 seleccion_excede_tope sin crear nada. 1–32.766 (part_number smallint: como mucho una parte por ítem más la 0/0); por defecto 10.000. Lo edita el Super Admin (HU #13420).';
COMMENT ON COLUMN tramites.consolidado_export_settings.is_active IS 'Interruptor del motor. false = no se crean lotes ni se reclaman ítems (semántica exacta en el backend).';

-- ════════════════════════════════════════════════════════════════════════════════════════════════
-- 2. tramites.consolidado_export_batches — cabecera del lote
-- ════════════════════════════════════════════════════════════════════════════════════════════════
CREATE TABLE IF NOT EXISTS tramites.consolidado_export_batches (
    id                      uuid        NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_consolidado_export_batches PRIMARY KEY (id),
    tenant_id               uuid        NULL,
    requested_by_user_id    uuid        NOT NULL,
    requested_role_code     text        NOT NULL,
    scope_tenant_id         uuid        NULL,
    network_scope           boolean     NOT NULL DEFAULT false,
    origin                  text        NOT NULL
        CONSTRAINT ck_consolidado_export_batches_origin CHECK (origin IN ('tramites', 'superadmin', 'ot_bandeja')),
    document_type           text        NOT NULL
        CONSTRAINT ck_consolidado_export_batches_document_type CHECK (document_type IN ('consolidado', 'consolidado_maestro')),
    selection_mode          text        NOT NULL
        CONSTRAINT ck_consolidado_export_batches_selection_mode CHECK (selection_mode IN ('ids', 'filtro')),
    status                  text        NOT NULL DEFAULT 'en_cola'
        CONSTRAINT ck_consolidado_export_batches_status CHECK (status IN (
            'en_cola', 'en_proceso', 'empaquetando',
            'completado', 'completado_con_omitidos', 'fallido', 'cancelado', 'expirado')),
    total_items             integer     NOT NULL,
    included_count          integer     NOT NULL DEFAULT 0,
    omitted_count           integer     NOT NULL DEFAULT 0,
    generated_count         integer     NOT NULL DEFAULT 0,
    parts_count             smallint    NOT NULL DEFAULT 0,
    dek_wrapped             bytea       NULL,
    effects_acknowledged_at timestamptz NOT NULL,
    last_claimed_at         timestamptz NULL,
    started_at              timestamptz NULL,
    finished_at             timestamptz NULL,
    expires_at              timestamptz NULL,
    purged_at               timestamptz NULL,
    error_code              text        NULL,
    ot_transit_office_id    uuid        NULL,
    created_at              timestamptz NOT NULL DEFAULT now(),
    created_by              uuid        NOT NULL,
    updated_at              timestamptz NULL,
    updated_by              uuid        NULL,
    deleted_at              timestamptz NULL,
    deleted_by              uuid        NULL,
    row_version             bigint      NOT NULL DEFAULT 0,

    CONSTRAINT fk_consolidado_export_batches_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_consolidado_export_batches_tenants_scope FOREIGN KEY (scope_tenant_id)
        REFERENCES identity.tenants (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_consolidado_export_batches_users_requested_by FOREIGN KEY (requested_by_user_id)
        REFERENCES identity.users (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_consolidado_export_batches_transit_offices FOREIGN KEY (ot_transit_office_id)
        REFERENCES catalogs.transit_offices (id) ON DELETE RESTRICT ON UPDATE RESTRICT,

    -- E5 (Q8): el único lote sin compañía es el de Super Admin, y el de Super Admin nunca lleva compañía
    -- (el acotamiento por X-Tenant-Id va en scope_tenant_id).
    CONSTRAINT ck_consolidado_export_batches_tenant_origin CHECK ((origin = 'superadmin') = (tenant_id IS NULL)),
    -- HU #13417 (A7.1): el scope es el X-Tenant-Id del Super Admin o la hija acotada de un lote de red.
    CONSTRAINT ck_consolidado_export_batches_scope_origin CHECK (scope_tenant_id IS NULL OR origin = 'superadmin' OR (origin = 'tramites' AND network_scope)),
    -- HU #13417: la vista de red es de una cabeza de compañía (Super Admin y OT no tienen red).
    CONSTRAINT ck_consolidado_export_batches_network_origin CHECK (NOT network_scope OR origin = 'tramites'),
    -- HU #13417: la hija acotada nunca es la propia compañía del lote (la cabeza acotada a sí misma es el lote propio).
    CONSTRAINT ck_consolidado_export_batches_network_child CHECK (scope_tenant_id IS NULL OR tenant_id IS NULL OR scope_tenant_id <> tenant_id),
    -- R-d (A5.5): todo lote de la bandeja del OT fija su organismo, y ningún otro origen lo lleva.
    CONSTRAINT ck_consolidado_export_batches_ot_origin CHECK ((origin = 'ot_bandeja') = (ot_transit_office_id IS NOT NULL)),

    CONSTRAINT ck_consolidado_export_batches_counts_non_negative CHECK (
        total_items >= 0 AND included_count >= 0 AND omitted_count >= 0
        AND generated_count >= 0 AND parts_count >= 0),
    CONSTRAINT ck_consolidado_export_batches_counts CHECK (included_count + omitted_count <= total_items),
    CONSTRAINT ck_consolidado_export_batches_generated CHECK (generated_count <= included_count),

    -- Máquina de estados: terminal ⇔ fecha de cierre; activo ⇒ DEK viva; purga = DEK destruida.
    CONSTRAINT ck_consolidado_export_batches_finished CHECK (
        (status IN ('en_cola', 'en_proceso', 'empaquetando')) = (finished_at IS NULL)),
    CONSTRAINT ck_consolidado_export_batches_dek_active CHECK (
        status NOT IN ('en_cola', 'en_proceso', 'empaquetando') OR dek_wrapped IS NOT NULL),
    CONSTRAINT ck_consolidado_export_batches_purged CHECK (purged_at IS NULL OR dek_wrapped IS NULL),
    CONSTRAINT ck_consolidado_export_batches_expired CHECK (status <> 'expirado' OR purged_at IS NOT NULL),
    CONSTRAINT ck_consolidado_export_batches_expires CHECK (finished_at IS NULL OR expires_at IS NOT NULL)
);

-- CF-17: 1 lote activo por usuario; el INSERT que choca → 409 lote_activo con el id del activo.
-- E4: por usuario, transversal a compañías (y Super Admin no tiene tenant_id).
CREATE UNIQUE INDEX IF NOT EXISTS uq_consolidado_export_batches_active_per_user
    ON tramites.consolidado_export_batches (requested_by_user_id)
    WHERE status IN ('en_cola', 'en_proceso', 'empaquetando') AND deleted_at IS NULL;

-- A9 (FK tenant) + listados por compañía.
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batches_tenant_created
    ON tramites.consolidado_export_batches (tenant_id, created_at DESC);

-- A9 (FK usuario) + "mis lotes" / lote retenido / GET actual. E4: el dueño se identifica por sub.
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batches_requested_by_created
    ON tramites.consolidado_export_batches (requested_by_user_id, created_at DESC);

-- A9 (FK de columnas opcionales): parciales con IS NOT NULL, utilizables por la verificación de la FK.
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batches_scope_tenant
    ON tramites.consolidado_export_batches (scope_tenant_id) WHERE scope_tenant_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batches_transit_office
    ON tramites.consolidado_export_batches (ot_transit_office_id) WHERE ot_transit_office_id IS NOT NULL;

-- E4: cola del worker entre tenants (round-robin CF-21), parcial y pequeño (precedente DDL 106).
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batches_claim
    ON tramites.consolidado_export_batches (last_claimed_at NULLS FIRST, created_at)
    WHERE status IN ('en_cola', 'en_proceso');

-- E4: purga entre tenants.
CREATE INDEX IF NOT EXISTS ix_consolidado_export_batches_purge
    ON tramites.consolidado_export_batches (expires_at)
    WHERE purged_at IS NULL AND expires_at IS NOT NULL;

ALTER TABLE tramites.consolidado_export_batches ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.consolidado_export_batches;
CREATE POLICY tenant_isolation ON tramites.consolidado_export_batches
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

DROP TRIGGER IF EXISTS tr_consolidado_export_batches_row_version ON tramites.consolidado_export_batches;
CREATE TRIGGER tr_consolidado_export_batches_row_version
    BEFORE UPDATE ON tramites.consolidado_export_batches
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

-- E6 (ADR-0070 adenda v6, hallazgo H1 del security-agent): el lote NO lleva tr_..._audit con trg_audit_log.
-- Esa función copia to_jsonb(NEW/OLD) completo a audit.audit_logs, incluida dek_wrapped; con el keyring de Data
-- Protection en la misma BD, quien lea la BD desenvolvería la DEK y la purga criptográfica (Q5) no serviría.
-- La traza Ley 1581 del lote es tramites.consolidado_export_audit (lote_creado/finalizado/cancelado/purgado), como E3.

COMMENT ON TABLE tramites.consolidado_export_batches IS
    'HU #13367 (Feature #13306, ADR-0070) — cabecera de un lote de descarga masiva de consolidados en ZIP. Máximo un lote activo por usuario (uq_consolidado_export_batches_active_per_user). RLS decorativa: el aislamiento es por requested_by_user_id = sub en el repositorio.';
COMMENT ON COLUMN tramites.consolidado_export_batches.tenant_id IS
    'Compañía del solicitante. NULL si y solo si origin = superadmin (Q8, ck_consolidado_export_batches_tenant_origin): el Super Admin no tiene compañía; las compañías alcanzadas quedan en la auditoría del lote (reached_tenant_ids) y en el tenant de cada ítem.';
COMMENT ON COLUMN tramites.consolidado_export_batches.scope_tenant_id IS 'Origen superadmin (FA2): compañía a la que se acotó la selección (X-Tenant-Id). Lote de red (network_scope, HU #13417): hija acotada, nunca la propia compañía. NULL = sin acotar.';
COMMENT ON COLUMN tramites.consolidado_export_batches.network_scope IS
    'Lote creado desde la vista de red de una cabeza (ADR-0070 v7, HU #13417): solo origen tramites, tenant_id = cabeza, scope_tenant_id = hija concreta o NULL = toda la red. Cada ítem conserva la compañía de su trámite.';
COMMENT ON COLUMN tramites.consolidado_export_batches.requested_role_code IS 'Rol con el que se creó el lote (para revalidación CF-16 y auditoría).';
COMMENT ON COLUMN tramites.consolidado_export_batches.total_items IS 'Total congelado al crear (ítems insertados en la misma transacción).';
COMMENT ON COLUMN tramites.consolidado_export_batches.generated_count IS 'v2: incluidos con delivery_mode = generado (primera generación). <= included_count.';
COMMENT ON COLUMN tramites.consolidado_export_batches.dek_wrapped IS 'Clave de datos del lote envuelta con Data Protection. NULL tras la purga o la cancelación = borrado criptográfico de las partes (Q5). Nunca sale a la auditoría genérica audit.audit_logs: la tabla no lleva trg_audit_log (E6, ADR-0070 adenda v6).';
COMMENT ON COLUMN tramites.consolidado_export_batches.effects_acknowledged_at IS 'Instante en que el usuario aceptó el texto de confirmación de CF-08 (Q1).';
COMMENT ON COLUMN tramites.consolidado_export_batches.last_claimed_at IS 'Último reclamo de un ítem del lote; base del round-robin entre lotes (CF-21).';
COMMENT ON COLUMN tramites.consolidado_export_batches.ot_transit_office_id IS 'Obligatorio si y solo si origin = ot_bandeja (#13308, ck_consolidado_export_batches_ot_origin): organismo fijado (transitOfficeIdOverride).';
