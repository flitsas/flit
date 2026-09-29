-- HU #12919 (Feature #12914, Épica #12718 DR. FLIT) — tope diario de mensajes del chat con LLM.
-- Migración: 20260925100000_HU12919_DrFlitDailyMessageUsage · ADR-0060 (Propuesto) §9.
--
-- Qué crea:
--   1. Schema dr_flit: bounded context nuevo de DR. FLIT (chat sobre el manual + casos de soporte).
--   2. dr_flit.daily_message_usage: un contador por (tenant_id, user_id, usage_date). usage_date es el día
--      calendario en hora Colombia (BogotaDays.Today() en la aplicación), nunca el día UTC. El tope se
--      cuenta por tenant Y usuario: un usuario con acceso a varias compañías no se autolimita entre ellas.
--      La aplicación incrementa con un único INSERT … ON CONFLICT … WHERE message_count < tope, así que
--      dos peticiones concurrentes no pueden pasarse del tope.
--
-- Excepciones documentadas (ADR-0060 §9.1):
--   · A6 sin soft delete: es un contador operativo de un día, no una entidad con ciclo de vida. Se purga por
--     retención, no se "elimina lógicamente".
--   · A16 sin trg_audit_log: A16 aplica a tablas de negocio, y auditar el contador escribiría una fila de
--     auditoría por cada mensaje del chat sin aportar trazabilidad. Sí lleva trg_row_version.
--
-- Idempotente (IF NOT EXISTS / DROP … IF EXISTS) y reversible (Down en la migración). RLS tenant_isolation
-- como el resto del repo, sin FORCE (la app conecta como owner y filtra por tenant_id explícito).

CREATE SCHEMA IF NOT EXISTS dr_flit;
COMMENT ON SCHEMA dr_flit IS
    'ADR-0060 (Épica #12718) · DR. FLIT: chat con LLM sobre el manual y escalación de casos de soporte a Azure DevOps.';

CREATE TABLE IF NOT EXISTS dr_flit.daily_message_usage (
    id            uuid        NOT NULL DEFAULT uuidv7(),
    tenant_id     uuid        NOT NULL,
    user_id       uuid        NOT NULL,
    usage_date    date        NOT NULL,
    message_count integer     NOT NULL DEFAULT 0,
    created_at    timestamptz NOT NULL DEFAULT now(),
    created_by    uuid        NULL,
    updated_at    timestamptz NOT NULL DEFAULT now(),
    updated_by    uuid        NULL,
    row_version   bigint      NOT NULL DEFAULT 0,

    CONSTRAINT pk_daily_message_usage PRIMARY KEY (id),
    CONSTRAINT fk_daily_message_usage_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants (id) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_daily_message_usage_users FOREIGN KEY (user_id)
        REFERENCES identity.users (id) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT uq_daily_message_usage_tenant_user_date UNIQUE (tenant_id, user_id, usage_date),
    CONSTRAINT ck_daily_message_usage_message_count CHECK (message_count >= 0)
);

-- Cobertura de las FK (A9). La de tenant_id la cubre el prefijo del UNIQUE; la de user_id no.
CREATE INDEX IF NOT EXISTS ix_daily_message_usage_user_id
    ON dr_flit.daily_message_usage (user_id);
-- Purga por retención (filas de días pasados).
CREATE INDEX IF NOT EXISTS ix_daily_message_usage_usage_date
    ON dr_flit.daily_message_usage (usage_date);

COMMENT ON TABLE dr_flit.daily_message_usage IS
    'ADR-0060 (HU #12919) · Mensajes enviados al LLM de DR. FLIT por (tenant, usuario, día Colombia). El tope sale de Anthropic:DrFlitDailyMessageLimit. Contador operativo: sin soft delete (A6) ni audit_log (A16), purgable por retención.';
COMMENT ON COLUMN dr_flit.daily_message_usage.tenant_id IS
    'Tenant activo del usuario al chatear (X-Tenant-Id). El tope se cuenta por tenant y usuario, nunca solo por usuario.';
COMMENT ON COLUMN dr_flit.daily_message_usage.user_id IS
    'Usuario que chatea (sub del JWT, identity.users.id).';
COMMENT ON COLUMN dr_flit.daily_message_usage.usage_date IS
    'Día calendario en hora Colombia (BogotaDays.Today()), no UTC. Cambio de día = fila nueva.';
COMMENT ON COLUMN dr_flit.daily_message_usage.message_count IS
    'Mensajes que llegaron al LLM ese día. Nunca supera el tope vigente al momento de incrementar.';
COMMENT ON COLUMN dr_flit.daily_message_usage.row_version IS
    'Token de concurrencia (public.trg_row_version).';

DROP TRIGGER IF EXISTS tr_daily_message_usage_row_version ON dr_flit.daily_message_usage;
CREATE TRIGGER tr_daily_message_usage_row_version BEFORE UPDATE ON dr_flit.daily_message_usage
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

ALTER TABLE dr_flit.daily_message_usage ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON dr_flit.daily_message_usage;
CREATE POLICY tenant_isolation ON dr_flit.daily_message_usage
    USING (
        current_setting('app.is_superadmin', true) = 'true'
        OR tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );
