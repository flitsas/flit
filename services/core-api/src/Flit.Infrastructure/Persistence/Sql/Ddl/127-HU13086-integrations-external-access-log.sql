-- HU #13086 (Feature #13067, Épica #12737) — Bitácora de accesos externos (Ley 1581).
--
-- Una fila por solicitud a /api/v1/external/*, atendida o rechazada (401, 403, 404, 423, 429, 5xx): quién
-- (client_id), qué endpoint, cuándo, desde qué IP, cuánto tardó y qué respondió. En el feed también qué se
-- entregó: rango de versiones, cantidad de ítems, compañías tocadas y si los datos personales iban en claro.
-- Permite reconstruir qué datos personales consultó cada cliente sin guardar los datos mismos.
--
-- NO guarda cuerpos de petición ni de respuesta, datos personales de los trámites, secretos ni pases (AC3).
-- La IP (AC1) sí es dato personal y va marcada @pii:medium.
--
-- Solo inserción: sin updated_*/deleted_*/row_version ni triggers de auditoría (una bitácora no se edita ni
-- se audita a sí misma). Excepción documentada en ADR-0067.
--
-- Plataforma, sin tenant_id ni RLS (como integrations.external_clients): el cliente no pertenece a una
-- compañía y una página del feed toca varias. Solo se inserta; la depuración a 12 meses (plan §6) queda
-- para la segunda fase, por eso el índice por occurred_at.
--
-- Idempotente y reaplicable.

CREATE TABLE IF NOT EXISTS integrations.external_access_log (
    id                uuid         NOT NULL DEFAULT uuidv7(),
    client_id         varchar(64),
    endpoint          varchar(80)  NOT NULL,
    request_id        varchar(64),
    ip                inet,
    sync_version_from bigint,
    sync_version_to   bigint,
    items_count       integer,
    tenant_ids        uuid[],
    pii_unmasked      boolean      NOT NULL DEFAULT false,
    http_status       integer      NOT NULL,
    duration_ms       integer      NOT NULL,
    occurred_at       timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT pk_external_access_log PRIMARY KEY (id),
    CONSTRAINT ck_external_access_log_http_status CHECK (http_status BETWEEN 100 AND 599),
    CONSTRAINT ck_external_access_log_duration CHECK (duration_ms >= 0),
    CONSTRAINT ck_external_access_log_items CHECK (items_count IS NULL OR items_count >= 0),
    CONSTRAINT ck_external_access_log_versions CHECK (
        (sync_version_from IS NULL) = (sync_version_to IS NULL))
);

COMMENT ON TABLE integrations.external_access_log IS
    'Bitácora de solicitudes a /api/v1/external/* (HU #13086, Ley 1581). Solo inserción. Sin cuerpos, datos personales de los trámites, secretos ni pases (la IP sí es dato personal: @pii:medium). Retención 12 meses (depuración: fase 2).';
COMMENT ON COLUMN integrations.external_access_log.client_id IS
    'Cliente del pase validado. En el canje del pase, el client_id solicitado (aún sin validar). NULL si la petición no trajo un pase válido.';
COMMENT ON COLUMN integrations.external_access_log.endpoint IS
    'token | tramites.sync | tramites.adjunto-url | otro (ruta inexistente bajo el prefijo).';
COMMENT ON COLUMN integrations.external_access_log.ip IS
    '@pii:medium — IP del cliente (primer salto de X-Forwarded-For o la conexión).';
COMMENT ON COLUMN integrations.external_access_log.sync_version_from IS
    'syncVersion del primer ítem entregado en la página (null si no hubo ítems).';
COMMENT ON COLUMN integrations.external_access_log.sync_version_to IS
    'syncVersion del último ítem entregado en la página (null si no hubo ítems).';
COMMENT ON COLUMN integrations.external_access_log.tenant_ids IS
    'Compañías (tenant_id) de los trámites entregados en la página, sin repetir.';
COMMENT ON COLUMN integrations.external_access_log.pii_unmasked IS
    'true si la respuesta llevó datos personales de compradores en claro (permiso external.tramites.pii.read).';

CREATE INDEX IF NOT EXISTS ix_external_access_log_client_occurred
    ON integrations.external_access_log (client_id, occurred_at DESC);
CREATE INDEX IF NOT EXISTS ix_external_access_log_occurred
    ON integrations.external_access_log (occurred_at);
