-- HU #12931 (Feature #12917, Épica #12718 DR. FLIT) — consentimiento de tratamiento de datos para el chat
-- con IA y los casos de soporte. Migración: 20260925130000_HU12931_DrFlitConsentAcceptances.
--
-- Qué crea: dr_flit.consent_acceptances, un registro por usuario y versión del texto aceptado, con la
-- evidencia de la aceptación (fecha, IP, user agent). Se acepta una vez por usuario y versión: cambiar la
-- versión configurada (DrFlit:Consent:Version) vuelve a pedirla a todos.
--
-- Es un registro de evidencia legal (Ley 1581 de 2012): no se modifica ni se elimina lógicamente.
-- Excepción documentada a A6 (sin deleted_at/deleted_by): borrar la evidencia de una autorización no es
-- una operación de negocio. Sí lleva trg_row_version y trg_audit_log (A16). RLS tenant_isolation como el
-- resto del repo, sin FORCE. Idempotente y reversible (Down en la migración).

CREATE SCHEMA IF NOT EXISTS dr_flit;

CREATE TABLE IF NOT EXISTS dr_flit.consent_acceptances (
    id              uuid         NOT NULL DEFAULT uuidv7(),
    tenant_id       uuid         NOT NULL,
    user_id         uuid         NOT NULL,
    consent_version varchar(50)  NOT NULL,
    accepted_at     timestamptz  NOT NULL DEFAULT now(),
    client_ip       varchar(64)  NULL,
    user_agent      varchar(512) NULL,
    created_at      timestamptz  NOT NULL DEFAULT now(),
    created_by      uuid         NULL,
    updated_at      timestamptz  NOT NULL DEFAULT now(),
    updated_by      uuid         NULL,
    row_version     bigint       NOT NULL DEFAULT 0,

    CONSTRAINT pk_consent_acceptances PRIMARY KEY (id),
    CONSTRAINT fk_consent_acceptances_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_consent_acceptances_users FOREIGN KEY (user_id)
        REFERENCES identity.users (id) ON DELETE CASCADE ON UPDATE CASCADE,
    -- Una aceptación por usuario y versión: el consentimiento es de la persona, no de la compañía.
    CONSTRAINT uq_consent_acceptances_user_version UNIQUE (user_id, consent_version),
    CONSTRAINT ck_consent_acceptances_version CHECK (char_length(trim(consent_version)) > 0)
);

CREATE INDEX IF NOT EXISTS ix_consent_acceptances_tenant_id ON dr_flit.consent_acceptances (tenant_id);

COMMENT ON TABLE dr_flit.consent_acceptances IS
    'ADR-0060 (HU #12931) · Aceptación del tratamiento de datos de DR. FLIT (Ley 1581 de 2012) antes de usar el chat con IA o radicar un caso. Una fila por usuario y versión. Evidencia legal: sin soft delete (excepción A6).';
COMMENT ON COLUMN dr_flit.consent_acceptances.tenant_id IS 'Tenant desde el que aceptó (referencia y RLS); la aceptación vale para la persona.';
COMMENT ON COLUMN dr_flit.consent_acceptances.consent_version IS 'Versión del texto aceptado (DrFlit:Consent:Version).';
COMMENT ON COLUMN dr_flit.consent_acceptances.client_ip IS '@pii:medium — IP desde la que aceptó (evidencia).';
COMMENT ON COLUMN dr_flit.consent_acceptances.user_agent IS '@pii:low — navegador desde el que aceptó (evidencia).';

DROP TRIGGER IF EXISTS tr_consent_acceptances_row_version ON dr_flit.consent_acceptances;
CREATE TRIGGER tr_consent_acceptances_row_version BEFORE UPDATE ON dr_flit.consent_acceptances
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

DROP TRIGGER IF EXISTS tr_consent_acceptances_audit ON dr_flit.consent_acceptances;
CREATE TRIGGER tr_consent_acceptances_audit AFTER INSERT OR UPDATE OR DELETE ON dr_flit.consent_acceptances
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

ALTER TABLE dr_flit.consent_acceptances ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON dr_flit.consent_acceptances;
CREATE POLICY tenant_isolation ON dr_flit.consent_acceptances
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );
