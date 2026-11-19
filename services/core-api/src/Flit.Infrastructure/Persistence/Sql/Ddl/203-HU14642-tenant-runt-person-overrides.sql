-- =============================================================================
-- HU #14642 — Overrides manuales de persona RUNT por tenant (SuperAdmin).
-- Permite que el SuperAdmin registre una respuesta de conductor manual para un
-- documento específico de una empresa, cortocircuitando la cadena remota RUNT
-- antes de llamar a Kyverum/Verifik.
--
-- Migración EF: 20261119000000_HU14642TenantRuntPersonOverrides
-- =============================================================================

CREATE TABLE IF NOT EXISTS admin.tenant_runt_person_overrides (
    id                 uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id          uuid        NOT NULL,

    -- Identificación del documento (tipos permitidos: CC, CE, PAS, TI, PPT)
    document_type      varchar(10) NOT NULL,
    document_number    text        NOT NULL,

    -- Nombre desglosado del conductor (misma semántica que DriverNameResolver)
    first_name         text        NOT NULL,
    second_name        text        NULL,
    first_last_name    text        NOT NULL,
    second_last_name   text        NULL,

    -- Estado de la persona y del conductor (mismos valores que el RUNT)
    citizen_status     text        NOT NULL DEFAULT 'ACTIVA',
    driver_status      text        NOT NULL DEFAULT 'ACTIVO',

    -- Estado de la licencia
    has_active_license boolean     NOT NULL DEFAULT true,
    license_categories text        NULL,

    -- Multas
    has_pending_fines  boolean     NOT NULL DEFAULT false,
    nro_paz_y_salvo    text        NULL,

    -- Metadatos del override
    reason             text        NOT NULL,
    is_active          boolean     NOT NULL DEFAULT true,
    expires_at         timestamptz NULL,

    -- Auditoría
    created_by         uuid        NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_by         uuid        NULL,
    updated_at         timestamptz NULL,

    CONSTRAINT pk_tenant_runt_person_overrides PRIMARY KEY (id),
    CONSTRAINT ck_tenant_runt_person_overrides_doc_type
        CHECK (document_type IN ('CC', 'CE', 'PAS', 'TI', 'PPT'))
);

-- Índice principal: por tenant para listar overrides de una empresa.
CREATE INDEX IF NOT EXISTS ix_tenant_runt_person_overrides_tenant_id
    ON admin.tenant_runt_person_overrides (tenant_id);

-- Índice de lookup: document_type + document_number normalizado dentro de un tenant.
-- Cubre la consulta de cortocircuito (tenant + tipo + número → override activo).
CREATE INDEX IF NOT EXISTS ix_tenant_runt_person_overrides_lookup
    ON admin.tenant_runt_person_overrides (tenant_id, upper(trim(document_type)), lower(trim(document_number)))
    WHERE is_active;

-- Unicidad: solo un override activo por (tenant, tipo doc, número doc). Normalizado para
-- evitar duplicados por diferencias de mayúsculas/espacios.
CREATE UNIQUE INDEX IF NOT EXISTS uq_tenant_runt_person_overrides_active
    ON admin.tenant_runt_person_overrides (tenant_id, upper(trim(document_type)), lower(trim(document_number)))
    WHERE is_active = true;

-- RLS: aislamiento por tenant (mismo patrón que tramites.persons y admin.*).
ALTER TABLE admin.tenant_runt_person_overrides ENABLE ROW LEVEL SECURITY;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_policies
        WHERE schemaname = 'admin'
          AND tablename  = 'tenant_runt_person_overrides'
          AND policyname = 'tenant_isolation')
    THEN
        CREATE POLICY tenant_isolation
            ON admin.tenant_runt_person_overrides
            USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
    END IF;
END $$;

COMMENT ON TABLE admin.tenant_runt_person_overrides IS
    'Overrides manuales de respuesta de conductor RUNT por tenant (HU #14642). '
    'El motor de consultas de core-api los evalúa ANTES de llamar a Kyverum/Verifik.';
