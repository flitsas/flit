-- HU #13084 (Feature #13065, Épica #12737) — Clientes de integración externos.
--
-- Usuario de máquina de un sistema externo (primer caso: Flito) para consumir los endpoints
-- /api/v1/external/*. Calca el modelo de ict.integration_clients (core-ict) —secreto generado por el
-- sistema y guardado solo como hash Argon2id, rotación con ventana, bloqueo por intentos, permisos—
-- con una diferencia de fondo: NO pertenece a una compañía. La lectura entre compañías la concede un
-- permiso del cliente y solo en el endpoint de sincronización (ámbito exclusivo, HU #13076).
-- No comparte tabla ni código con core-ict.
--
-- Sin tenant_id ni RLS: es una entidad de plataforma, como admin.banners (ADR-0058) o
-- ict.integration_clients. La administran solo superadministradores (HU #13088) y el login busca por
-- client_id sin conocer compañía alguna.
--
-- Idempotente y reaplicable.

CREATE SCHEMA IF NOT EXISTS integrations;
COMMENT ON SCHEMA integrations IS
    'Integraciones de sistemas externos con la plataforma (Épica #12737): clientes de máquina y su bitácora de acceso.';

CREATE TABLE IF NOT EXISTS integrations.external_clients (
    id                   uuid         NOT NULL DEFAULT uuidv7(),
    client_id            varchar(64)  NOT NULL,
    display_name         varchar(120) NOT NULL,
    purpose              varchar(300) NOT NULL,
    secret_hash          text         NOT NULL,
    previous_secret_hash text,
    secret_rotated_at    timestamptz,
    must_rotate          boolean      NOT NULL DEFAULT false,
    scopes               jsonb        NOT NULL DEFAULT '[]'::jsonb,
    is_active            boolean      NOT NULL DEFAULT true,
    failed_attempts      integer      NOT NULL DEFAULT 0,
    locked_until         timestamptz,
    last_token_at        timestamptz,
    row_version          bigint       NOT NULL DEFAULT 0,
    created_at           timestamptz  NOT NULL DEFAULT now(),
    created_by           uuid,
    updated_at           timestamptz,
    updated_by           uuid,
    deleted_at           timestamptz,
    deleted_by           uuid,
    CONSTRAINT pk_external_clients PRIMARY KEY (id),
    -- Un identificador no se reutiliza nunca, ni tras dar de baja al cliente: la bitácora lo referencia.
    CONSTRAINT uq_external_clients_client_id UNIQUE (client_id),
    CONSTRAINT ck_external_clients_client_id_formato CHECK (client_id ~ '^[a-z0-9][a-z0-9-]{2,63}$'),
    CONSTRAINT ck_external_clients_scopes_array CHECK (jsonb_typeof(scopes) = 'array'),
    CONSTRAINT ck_external_clients_failed_attempts CHECK (failed_attempts >= 0)
);

COMMENT ON TABLE integrations.external_clients IS
    'Clientes de integración externos (HU #13084). Tabla de PLATAFORMA sin tenant_id ni RLS, como admin.banners (ADR-0058) e ict.integration_clients: el cliente no pertenece a una compañía; la lectura entre compañías la concede su permiso en el endpoint de sincronización.';
COMMENT ON COLUMN integrations.external_clients.client_id IS
    'Identificador público del cliente (p. ej. flito-dev). Único y no reutilizable.';
COMMENT ON COLUMN integrations.external_clients.purpose IS
    'Finalidad del tratamiento de datos personales (Ley 1581).';
COMMENT ON COLUMN integrations.external_clients.secret_hash IS
    'Hash Argon2id del secreto. El secreto en claro no se guarda en ningún sitio. @pii:high';
COMMENT ON COLUMN integrations.external_clients.previous_secret_hash IS
    'Hash del secreto anterior, válido durante la ventana de gracia de una rotación. @pii:high';
COMMENT ON COLUMN integrations.external_clients.scopes IS
    'Permisos del cliente como arreglo JSON, p. ej. ["external.tramites.read","external.tramites.pii.read"].';

DROP TRIGGER IF EXISTS tr_external_clients_row_version ON integrations.external_clients;
CREATE TRIGGER tr_external_clients_row_version BEFORE UPDATE ON integrations.external_clients
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_external_clients_audit ON integrations.external_clients;
CREATE TRIGGER tr_external_clients_audit AFTER INSERT OR UPDATE OR DELETE ON integrations.external_clients
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
