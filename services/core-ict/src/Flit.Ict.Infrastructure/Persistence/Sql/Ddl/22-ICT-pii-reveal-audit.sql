-- =============================================================================
-- core-ict — Auditoría de revelado de datos personales (HU #11820, Feature #11814).
--
-- La Trazabilidad ICT muestra la PII enmascarada SIEMPRE. Verla en claro es una
-- acción explícita y deja rastro aquí. Sin este registro el enmascarado sería una
-- cortina: cualquiera con acceso al módulo podría leer los datos de cualquier
-- ciudadano sin que quede constancia de quién lo hizo ni sobre qué trámite.
--
-- Tabla de PLATAFORMA y no de negocio: la escribe el propio servicio, nunca el
-- cliente de integración. Lleva tenant_id porque el dato revelado pertenece a un
-- tenant, pero NO lleva RLS: quien audita es el responsable de protección de
-- datos, que necesita ver todos los tenants, y el filtrado se hace en la consulta.
--
-- Deliberadamente NO guarda el valor revelado. Registrar el dato en claro para
-- auditar quién lo vio en claro multiplicaría el problema en vez de controlarlo.
-- =============================================================================

CREATE TABLE IF NOT EXISTS ict.pii_reveal_audit (
    id             uuid NOT NULL DEFAULT uuidv7(),
    tenant_id      uuid NOT NULL,
    master_id      uuid NOT NULL,
    -- Número FLIT del trámite: se guarda además del id para que la auditoría se
    -- pueda leer sin cruzar tablas, incluso si el pre-trámite se purga.
    transaction_number bigint NOT NULL,
    -- Sujeto (sub) del JWT de quien lo pidió, y su identificación legible.
    requested_by   varchar(120) NOT NULL,
    requested_role varchar(60) NOT NULL DEFAULT '',
    -- Qué se reveló: 'actores' hoy; el campo deja sitio a futuros ámbitos sin migrar.
    scope          varchar(40) NOT NULL DEFAULT 'actores',
    requested_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT pk_pii_reveal_audit PRIMARY KEY (id)
);

-- La consulta natural del auditor es «qué se reveló de este trámite» y «qué reveló
-- esta persona»; se indexan las dos.
CREATE INDEX IF NOT EXISTS ix_pii_reveal_audit_tramite
    ON ict.pii_reveal_audit (master_id, requested_at DESC);

CREATE INDEX IF NOT EXISTS ix_pii_reveal_audit_usuario
    ON ict.pii_reveal_audit (requested_by, requested_at DESC);
