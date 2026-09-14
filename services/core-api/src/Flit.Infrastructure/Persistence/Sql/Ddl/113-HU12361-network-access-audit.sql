-- HU #12361 (Feature #12257, épica #12235) — auditoría del acceso consolidado de una cabeza de red
-- a los datos de sus clientes hijos (listados, estadísticas, detalle y descargas de documentos).
-- Migración: 20260914120000_HU12361_NetworkAccessAudit
--
-- Diseño (plan bloque B, decisión 4):
--  * UN registro por PETICIÓN (AC8), nunca uno por trámite: reached_tenant_ids lleva los hijos
--    DISTINTOS alcanzados por el resultado; filters solo identificadores y valores de filtro (sin PII).
--  * Solo se escribe cuando el resultado incluye al menos una fila de un hijo (AC5): el acceso a
--    datos propios no infla el registro. Un cliente sin jerarquía jamás escribe aquí (AC6).
--  * APPEND-ONLY: UPDATE y DELETE rechazados por tr_network_access_audit_immutable (mismo patrón que
--    identity.tenant_hierarchy_audit). row_version se conserva por convención A5 del checklist, pero
--    ninguna fila se actualiza jamás (no hay trigger BEFORE UPDATE porque el UPDATE está prohibido).
--  * EXCEPCIÓN DOCUMENTADA al checklist A7/A8/A9: actor_tenant_id, reached_tenant_ids y
--    procedure_tenant_id NO llevan FK a identity.tenants ni a tramites.procedure_instances. El
--    registro debe sobrevivir al desvínculo del hijo y a cualquier borrado futuro (AC3); una FK con
--    CASCADE destruiría la traza y una RESTRICT impediría el borrado.
--  * RLS: la fila pertenece a varios tenants a la vez (la cabeza que accedió y los hijos alcanzados).
--    La política deja leer al hijo dueño (procedure_tenant_id), a cualquier hijo alcanzado
--    (= ANY(reached_tenant_ids)) y a la cabeza actora. Sin FORCE (convención vigente del repo:
--    la app conecta como owner y el acceso lo gobierna la aplicación; SuperAdmin lee todo).
--  * No lleva trg_audit_log: la propia tabla es la auditoría (como tenant_hierarchy_audit).

CREATE TABLE IF NOT EXISTS tramites.network_access_audit (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_network_access_audit PRIMARY KEY (id),
    occurred_at timestamptz NOT NULL DEFAULT now(),
    actor_user_id uuid NULL,
    actor_tenant_id uuid NOT NULL,
    reached_tenant_ids uuid[] NOT NULL,
    resource text NOT NULL,
    filters jsonb NULL,
    procedure_id uuid NULL,
    procedure_tenant_id uuid NULL,
    attachment_id uuid NULL,
    result text NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    CONSTRAINT ck_network_access_audit_reached_not_empty CHECK (cardinality(reached_tenant_ids) > 0),
    CONSTRAINT ck_network_access_audit_result CHECK (result IN ('ok', 'forbidden', 'not_found')),
    CONSTRAINT ck_network_access_audit_resource CHECK (resource ~ '^network\.[a-z_]+\.[a-z_]+$')
);

CREATE INDEX IF NOT EXISTS ix_network_access_audit_procedure_tenant_occurred_at
    ON tramites.network_access_audit (procedure_tenant_id, occurred_at DESC);

CREATE INDEX IF NOT EXISTS ix_network_access_audit_actor_tenant_occurred_at
    ON tramites.network_access_audit (actor_tenant_id, occurred_at DESC);

CREATE INDEX IF NOT EXISTS ix_network_access_audit_reached_tenant_ids
    ON tramites.network_access_audit USING gin (reached_tenant_ids);

-- Append-only forzado por la base: ni la aplicación ni un UPDATE manual reescriben la historia.
CREATE OR REPLACE FUNCTION tramites.trg_network_access_audit_immutable() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'tramites.network_access_audit es append-only: no se permite % (id=%)', TG_OP, OLD.id
        USING ERRCODE = 'check_violation';
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_network_access_audit_immutable ON tramites.network_access_audit;
CREATE TRIGGER tr_network_access_audit_immutable
    BEFORE UPDATE OR DELETE ON tramites.network_access_audit
    FOR EACH ROW EXECUTE FUNCTION tramites.trg_network_access_audit_immutable();

ALTER TABLE tramites.network_access_audit ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS tenant_isolation ON tramites.network_access_audit;
CREATE POLICY tenant_isolation ON tramites.network_access_audit
    USING (
        NULLIF(current_setting('app.is_superadmin', true), '') = 'true'
        OR procedure_tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
        OR NULLIF(current_setting('app.current_tenant_id', true), '')::uuid = ANY (reached_tenant_ids)
        OR actor_tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid
    );

COMMENT ON TABLE tramites.network_access_audit IS
    'HU #12361 (Feature #12257) — auditoría append-only del acceso consolidado de una cabeza de red a '
    'datos de sus clientes hijos: un registro por petición (listado, estadística, detalle o descarga) '
    'con el actor, la cabeza, los hijos alcanzados, el recurso, el instante y el resultado. Solo '
    'identificadores: nunca datos personales del trámite. SIN FK a identity.tenants ni a '
    'tramites.procedure_instances (excepción documentada A7/A8/A9): la fila sobrevive al desvínculo. '
    'UPDATE/DELETE rechazados por tr_network_access_audit_immutable.';

COMMENT ON COLUMN tramites.network_access_audit.occurred_at IS
    'Instante en que se entregó la respuesta auditada (UTC).';

COMMENT ON COLUMN tramites.network_access_audit.actor_user_id IS
    'Usuario de la cabeza de red que ejecutó la consulta o descarga (identity.users.id, sin FK). NULL = sin identidad resoluble.';

COMMENT ON COLUMN tramites.network_access_audit.actor_tenant_id IS
    'Cliente cabeza de red al que pertenece el actor (identity.tenants.id, sin FK a propósito).';

COMMENT ON COLUMN tramites.network_access_audit.reached_tenant_ids IS
    'Clientes hijos DISTINTOS cuyas filas aparecieron en el resultado (o el hijo dueño en detalle/descarga, o el hijo pedido en un rechazo). Nunca vacío; nunca incluye a la propia cabeza.';

COMMENT ON COLUMN tramites.network_access_audit.resource IS
    'Recurso consultado: network.instances.search | network.instances.detail | network.stats.overview | network.attachments.list | network.attachments.download.';

COMMENT ON COLUMN tramites.network_access_audit.filters IS
    'Filtros aplicados a la consulta (solo identificadores y valores de filtro: estado, modalidad, tipo, rango de fechas, paginación, cliente hijo). NUNCA placa, documento, nombre ni ningún dato personal.';

COMMENT ON COLUMN tramites.network_access_audit.procedure_id IS
    'Trámite consultado o del que se descargó un documento (tramites.procedure_instances.id, sin FK). NULL en listados y estadísticas.';

COMMENT ON COLUMN tramites.network_access_audit.procedure_tenant_id IS
    'Cliente hijo dueño del trámite consultado (identity.tenants.id, sin FK). NULL en listados y estadísticas.';

COMMENT ON COLUMN tramites.network_access_audit.attachment_id IS
    'Documento descargado o visualizado (tramites.procedure_instance_attachments.id, sin FK). NULL salvo en network.attachments.download.';

COMMENT ON COLUMN tramites.network_access_audit.result IS
    'Desenlace de la petición: ok | forbidden (rechazada por autorización: también se registra el intento) | not_found.';

COMMENT ON COLUMN tramites.network_access_audit.row_version IS
    'Convención A5 del checklist. La tabla es append-only: ninguna fila cambia jamás de versión.';
