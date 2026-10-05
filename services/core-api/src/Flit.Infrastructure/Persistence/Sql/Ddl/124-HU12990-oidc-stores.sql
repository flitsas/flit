-- HU #12990 (Feature #12886, FLIT Suite, tarea A-05) — almacenes del servidor OIDC del hub (OpenIddict 7.x).
-- Migración: 20260928120000_HU12990_OidcStores.
--
-- Mismas tablas, columnas e índices que genera OpenIddict.EntityFrameworkCore con UseOpenIddict<Guid>() y la
-- convención snake_case del repo (verificado en la espiga A-04). Se escriben en DDL, como el resto del esquema,
-- porque el snapshot de EF del repo está desfasado y una migración generada arrastraría cambios ajenos.
--
-- Tablas globales, sin tenant_id ni RLS: los clientes son productos y servicios, no empresas. identity.oidc_tokens
-- crece con cada código y refresh token; la limpia OidcPruningService. Idempotente.

CREATE SCHEMA IF NOT EXISTS identity;

CREATE TABLE IF NOT EXISTS identity.oidc_applications (
    id                        uuid                  NOT NULL,
    application_type          character varying(50),
    client_id                 character varying(100),
    client_secret             text,
    client_type               character varying(50),
    concurrency_token         character varying(50),
    consent_type              character varying(50),
    display_name              text,
    display_names             text,
    json_web_key_set          text,
    permissions               text,
    post_logout_redirect_uris text,
    properties                text,
    redirect_uris             text,
    requirements              text,
    settings                  text,
    CONSTRAINT pk_oidc_applications PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS identity.oidc_scopes (
    id                uuid                   NOT NULL,
    concurrency_token character varying(50),
    description       text,
    descriptions      text,
    display_name      text,
    display_names     text,
    name              character varying(200),
    properties        text,
    resources         text,
    CONSTRAINT pk_oidc_scopes PRIMARY KEY (id)
);

CREATE TABLE IF NOT EXISTS identity.oidc_authorizations (
    id                uuid                   NOT NULL,
    application_id    uuid,
    concurrency_token character varying(50),
    creation_date     timestamp with time zone,
    properties        text,
    scopes            text,
    status            character varying(50),
    subject           character varying(400),
    type              character varying(50),
    CONSTRAINT pk_oidc_authorizations PRIMARY KEY (id),
    CONSTRAINT fk_oidc_authorizations_oidc_applications_application_id
        FOREIGN KEY (application_id) REFERENCES identity.oidc_applications (id)
);

CREATE TABLE IF NOT EXISTS identity.oidc_tokens (
    id                uuid                    NOT NULL,
    application_id    uuid,
    authorization_id  uuid,
    concurrency_token character varying(50),
    creation_date     timestamp with time zone,
    expiration_date   timestamp with time zone,
    payload           text,
    properties        text,
    redemption_date   timestamp with time zone,
    reference_id      character varying(100),
    status            character varying(50),
    subject           character varying(400),
    type              character varying(150),
    CONSTRAINT pk_oidc_tokens PRIMARY KEY (id),
    CONSTRAINT fk_oidc_tokens_oidc_applications_application_id
        FOREIGN KEY (application_id) REFERENCES identity.oidc_applications (id),
    CONSTRAINT fk_oidc_tokens_oidc_authorizations_authorization_id
        FOREIGN KEY (authorization_id) REFERENCES identity.oidc_authorizations (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_oidc_applications_client_id ON identity.oidc_applications (client_id);
CREATE INDEX IF NOT EXISTS ix_oidc_authorizations_application_id_status_subject_type
    ON identity.oidc_authorizations (application_id, status, subject, type);
CREATE UNIQUE INDEX IF NOT EXISTS ix_oidc_scopes_name ON identity.oidc_scopes (name);
CREATE INDEX IF NOT EXISTS ix_oidc_tokens_application_id_status_subject_type
    ON identity.oidc_tokens (application_id, status, subject, type);
CREATE INDEX IF NOT EXISTS ix_oidc_tokens_authorization_id ON identity.oidc_tokens (authorization_id);
CREATE UNIQUE INDEX IF NOT EXISTS ix_oidc_tokens_reference_id ON identity.oidc_tokens (reference_id);

COMMENT ON TABLE identity.oidc_applications IS
    'HU #12990 (A-05) · Clientes OIDC: un producto (plataforma, tramites, demo…) o un servicio (svc-<código>). Los sincroniza OidcClientSync desde Suite:Oidc:Clients.';
COMMENT ON COLUMN identity.oidc_applications.client_secret IS
    '@pii:none · Hash del secreto de un cliente de servicio (OpenIddict lo guarda hasheado, nunca en claro).';
COMMENT ON TABLE identity.oidc_authorizations IS
    'HU #12990 (A-05) · Autorizaciones OIDC (usuario × cliente). subject = id del usuario.';
COMMENT ON TABLE identity.oidc_tokens IS
    'HU #12990 (A-05) · Códigos de autorización y refresh tokens. Crece con cada login; OidcPruningService borra los vencidos.';
COMMENT ON TABLE identity.oidc_scopes IS
    'HU #12990 (A-05) · Scopes OIDC registrados en base (hoy vacía: los scopes se declaran en el servidor).';
