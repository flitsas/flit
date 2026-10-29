-- HU #14067 (Feature H, Épica #14021) — Integración externa «Persona Natural» ligada a la compañía.
--
-- integrations.external_clients (DDL 125) gana dos columnas ADITIVAS:
--   tipo        'externa' (valor por defecto: todas las filas existentes, sin compañía) | 'persona_natural'.
--   company_id  compañía a la que se liga la integración (identity.tenants). NULL para las 'externa'.
-- Una sola integración ACTIVA por (compañía, tipo): índice único parcial. Una desactivada o dada de baja libera el cupo.
-- Las filas actuales no cambian de comportamiento: quedan tipo='externa' y sin compañía.
--
-- Idempotente y reaplicable.

ALTER TABLE integrations.external_clients
    ADD COLUMN IF NOT EXISTS tipo       varchar(32) NOT NULL DEFAULT 'externa',
    ADD COLUMN IF NOT EXISTS company_id uuid;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_external_clients_tipo') THEN
        ALTER TABLE integrations.external_clients
            ADD CONSTRAINT ck_external_clients_tipo CHECK (tipo IN ('externa', 'persona_natural'));
    END IF;
    -- Un cliente de tipo persona_natural siempre pertenece a una compañía.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_external_clients_company_por_tipo') THEN
        ALTER TABLE integrations.external_clients
            ADD CONSTRAINT ck_external_clients_company_por_tipo CHECK (tipo = 'externa' OR company_id IS NOT NULL);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_external_clients_company') THEN
        ALTER TABLE integrations.external_clients
            ADD CONSTRAINT fk_external_clients_company FOREIGN KEY (company_id) REFERENCES identity.tenants (id);
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_external_clients_company_tipo_activo
    ON integrations.external_clients (company_id, tipo)
    WHERE is_active AND deleted_at IS NULL AND company_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_external_clients_company
    ON integrations.external_clients (company_id)
    WHERE company_id IS NOT NULL AND deleted_at IS NULL;

COMMENT ON COLUMN integrations.external_clients.tipo IS
    'Tipo de integración: externa (feed de trámites, sin compañía) o persona_natural (portal de Persona Natural, ligada a una compañía). HU #14067.';
COMMENT ON COLUMN integrations.external_clients.company_id IS
    'Compañía a la que se liga la integración (identity.tenants). NULL en las de tipo externa. HU #14067.';
