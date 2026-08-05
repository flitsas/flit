-- Causales de rechazo: catálogo global (SuperAdmin) + causales elegidas en cada decisión del OT.
--
-- ── Por qué una tabla puente y no jsonb en el historial ─────────────────────────
-- El motivo del rechazo ya viaja como texto en `procedure_instance_status_history.reason` y el
-- checklist de subsanación va en su `metadata`. Ninguno de los dos es agregable: el propósito de
-- estas causales es EXACTAMENTE el reporte de motivos (Pareto por organismo/empresa/tipo), y eso
-- pide una fila por (rechazo, causal) indexable, no un array dentro de un jsonb.
--
-- El texto libre NO desaparece: sigue en `reason` como observación general del rechazo. Son cosas
-- distintas y complementarias — la causal dice QUÉ falló (agregable), la observación dice CÓMO
-- corregirlo (contexto para quien subsana). Marcar varias causales es válido y esperado.
--
-- ── Por qué se cuelga del historial y no del trámite ────────────────────────────
-- Un expediente puede rechazarse varias veces (ciclos de subsanación). Las causales pertenecen al
-- EVENTO de rechazo, no al trámite: colgarlas del trámite mezclaría los ciclos y haría imposible
-- distinguir «lo rechacé por A, se subsanó, y ahora lo rechazo por B».
--
-- ── Lección de FLIT 1 ───────────────────────────────────────────────────────────
-- Allí el catálogo existía (rejection_type_master) y el reporte igual no sirvió: se guardaban las
-- 9 causales en cada rechazo (5.484 de 5.653) y ninguna observación. La respuesta NO es restringir
-- la captura —marcar varias es legítimo— sino poder VER el mal uso: por eso el reporte expone
-- «causales por rechazo (promedio)». Si ese número se acerca al tamaño del catálogo, alguien está
-- marcando todo y las métricas dejan de servir.
-- DDL IDEMPOTENTE.

-- ── Catálogo global de causales ─────────────────────────────────────────────────
-- Sin tenant_id: es catálogo global administrado por SuperAdmin (mismo criterio que ADR-0019 para
-- los catálogos de trámites). Que cada organismo definiera las suyas haría incomparable el reporte
-- entre organismos y entre empresas, que es justamente lo que se quiere comparar.
CREATE TABLE IF NOT EXISTS catalogs.rejection_reasons (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_rejection_reasons PRIMARY KEY (id),
    -- Código estable: es la llave que usan reportes y seeds; la descripción puede reescribirse sin
    -- romper series históricas.
    code varchar(60) NOT NULL,
    description varchar(150) NOT NULL,
    -- Las causales dependen del proceso: «manifiesto de aduana» no aplica a un traspaso ni
    -- «escritura del vendedor» a una matrícula inicial. Valores de TramiteModalidadEntrada.
    modalidad varchar(40) NOT NULL,
    CONSTRAINT ck_rejection_reasons_modalidad
        CHECK (modalidad IN ('matricula_inicial', 'traspaso')),
    sort_order integer NOT NULL DEFAULT 0,
    -- Baja lógica y no borrado: una causal retirada debe seguir resolviendo el nombre de los
    -- rechazos históricos que la usaron.
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_rejection_reasons_code
    ON catalogs.rejection_reasons(code);

-- La consulta del modal de rechazo: causales activas de la modalidad, en orden de presentación.
CREATE INDEX IF NOT EXISTS ix_rejection_reasons_modalidad
    ON catalogs.rejection_reasons(modalidad, is_active, sort_order);

COMMENT ON TABLE catalogs.rejection_reasons IS
    'Catálogo global de causales de rechazo administrado por SuperAdmin. El revisor del organismo '
    'puede marcar varias en un mismo rechazo; la observación en texto libre las acompaña.';

-- ── Causales elegidas en cada rechazo ───────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tramites.procedure_instance_rejection_reasons (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_rejection_reasons PRIMARY KEY (id),
    tenant_id uuid NOT NULL,
    procedure_instance_id uuid NOT NULL
        CONSTRAINT fk_pirr_procedure_instance
        REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE,
    -- Evento de rechazo al que pertenecen. Nullable por defensa: si alguna vez se registra una
    -- causal fuera de una transición, la fila no se pierde.
    status_history_id uuid
        CONSTRAINT fk_pirr_status_history
        REFERENCES tramites.procedure_instance_status_history(id) ON DELETE CASCADE,
    rejection_reason_id uuid NOT NULL
        CONSTRAINT fk_pirr_rejection_reason
        REFERENCES catalogs.rejection_reasons(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid
);

-- Una causal no puede repetirse dentro del mismo rechazo (si el cliente manda duplicados, la
-- segunda inserción falla en vez de inflar el conteo del reporte).
CREATE UNIQUE INDEX IF NOT EXISTS uq_pirr_event_reason
    ON tramites.procedure_instance_rejection_reasons(status_history_id, rejection_reason_id)
    WHERE status_history_id IS NOT NULL;

-- Agregación del reporte: «motivos de los rechazos de este tenant en este rango».
CREATE INDEX IF NOT EXISTS ix_pirr_tenant_reason
    ON tramites.procedure_instance_rejection_reasons(tenant_id, rejection_reason_id);

-- Detalle por expediente (timeline del trámite y ficha de subsanación).
CREATE INDEX IF NOT EXISTS ix_pirr_instance
    ON tramites.procedure_instance_rejection_reasons(procedure_instance_id);

-- RLS por tenant, igual que el resto de tablas de `tramites`. La lectura cross-tenant del
-- organismo pasa por `SET LOCAL row_security = off` dentro del scope autorizado por grant, que es
-- el mismo mecanismo con el que ya lee los trámites de sus empresas.
ALTER TABLE tramites.procedure_instance_rejection_reasons ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS tenant_isolation ON tramites.procedure_instance_rejection_reasons;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_rejection_reasons
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

COMMENT ON TABLE tramites.procedure_instance_rejection_reasons IS
    'Causales del catálogo marcadas por el organismo en un evento de rechazo concreto '
    '(status_history_id). Varias por rechazo es válido y esperado.';
