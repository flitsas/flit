-- Epic #12543 — Aceptación de Términos y Condiciones al crear un trámite.
-- Migración: 20260914200000_E12543_ProcedureTermsAcceptances
--
-- Evidencia legal, no bitácora técnica: una fila por cada vez que un usuario marca el checkbox
-- y pulsa Continuar, ANTES de que se le muestre el formulario de radicación (RN-03). Por eso no
-- va SOLO al rastro administrativo unificado (admin.tenant_config_audit_logs): ese writer es
-- best-effort y se traga los fallos, y aquí un INSERT fallido tiene que devolver error para que
-- el frontend NO habilite el formulario. Al rastro unificado se refleja además una fila
-- (module = tramites, operation = accept_terms) para que se vea en la pantalla de Auditoría.
--
-- Se pide en cada creación (RN-05): no hay unicidad por usuario ni por tipo. Append-only.
--
-- tenant_id NULLABLE: el SuperAdmin puede abrir el asistente sin acotar compañía y su aceptación
-- también cuenta. user_id NOT NULL y con FK (RESTRICT): sin usuario real no hay aceptación que
-- registrar, y borrar al usuario no puede borrar su evidencia.
-- terms_url: qué documento aceptó; si el enlace cambia, las filas viejas siguen diciendo cuál era.

CREATE TABLE IF NOT EXISTS tramites.procedure_terms_acceptances (
    id                   uuid         NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_terms_acceptances PRIMARY KEY (id),
    tenant_id            uuid         NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    user_id              uuid         NOT NULL REFERENCES identity.users(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_type_code  varchar(50)  NOT NULL,
    terms_url            text         NOT NULL,
    accepted_at          timestamptz  NOT NULL DEFAULT now(),
    client_ip            varchar(64)  NULL,
    user_agent           text         NULL
);

CREATE INDEX IF NOT EXISTS ix_procedure_terms_acceptances_user_accepted
    ON tramites.procedure_terms_acceptances (user_id, accepted_at DESC);
CREATE INDEX IF NOT EXISTS ix_procedure_terms_acceptances_tenant_accepted
    ON tramites.procedure_terms_acceptances (tenant_id, accepted_at DESC);

COMMENT ON TABLE tramites.procedure_terms_acceptances IS
    'Epic #12543: aceptación de Términos y Condiciones por cada creación de trámite (usuario, fecha UTC, tipo de trámite, IP).';
COMMENT ON COLUMN tramites.procedure_terms_acceptances.procedure_type_code IS
    'Code del tipo de trámite (tramites.procedure_types.code) que el usuario iba a crear.';
COMMENT ON COLUMN tramites.procedure_terms_acceptances.terms_url IS
    'URL del documento de T&C vigente en el momento de aceptar.';

ALTER TABLE tramites.procedure_terms_acceptances ENABLE ROW LEVEL SECURITY;

-- Igual que admin.tenant_config_audit_logs (32-HU10678): SuperAdmin ve todo, y las filas sin
-- tenant (SuperAdmin sin compañía acotada) también entran.
DROP POLICY IF EXISTS tenant_isolation ON tramites.procedure_terms_acceptances;
CREATE POLICY tenant_isolation ON tramites.procedure_terms_acceptances
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id IS NULL
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

DROP TRIGGER IF EXISTS tr_procedure_terms_acceptances_audit ON tramites.procedure_terms_acceptances;
CREATE TRIGGER tr_procedure_terms_acceptances_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_terms_acceptances
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
