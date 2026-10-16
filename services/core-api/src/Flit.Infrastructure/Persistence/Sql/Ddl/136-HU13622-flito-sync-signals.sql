-- HU #13622 (Feature #13616, Épica #13614) — Registro coalescente de avisos a FLITO.
--
-- Cada vez que un trámite recibe una sync_version nueva (DDL 122/123: una vez por transacción y trámite, también
-- cuando cambia una tabla hija) queda, en la MISMA transacción, un aviso pendiente para FLITO. El despachador
-- (HU #13624) lo convierte en un webhook firmado; el aviso es solo una señal: FLITO trae el dato del feed v3.1.
--
-- Una fila por trámite (coalescente): varios cambios antes del despacho dejan una sola fila con la versión más
-- reciente; un cambio después del despacho la vuelve a dejar pendiente.
--
-- ── Excepciones a la checklist de schema, a propósito ──────────────────────────────────────────────────────────
-- * PK = procedure_instance_id y SIN FK hacia tramites.procedure_instances: una FK tomaría FOR KEY SHARE sobre el
--   trámite en cada aviso y reabriría el cruce de bloqueos que corrigió el DDL 128 (deadlock 40P01).
-- * Sin RLS ni columnas de auditoría/soft delete: es una cola técnica de la plataforma, no una entidad de negocio,
--   y la lee el despachador para TODAS las compañías (lectura entre compañías acotada, ADR-0069).
-- * El índice de la cola (ix_flito_sync_signals_pendientes) no empieza por tenant_id: la cola es de todas las
--   compañías y se recorre en el orden en que maduran los cambios.
-- * Sin purga: queda una fila por trámite (la de un trámite borrado físicamente, ya cerrada, también). Es una fila
--   pequeña por trámite; si algún día pesa, se borran las que no están pendientes.
-- * Los migrados desde FLIT 1 (is_migrated) no generan aviso, igual que no entran al feed. La regla «radicado al
--   menos una vez» NO se evalúa aquí (al sellar, la fila del historial puede no existir todavía en la
--   transacción): la aplica el despachador.
--
-- Idempotente y reaplicable.

CREATE SCHEMA IF NOT EXISTS integrations;

CREATE TABLE IF NOT EXISTS integrations.flito_sync_signals (
    procedure_instance_id uuid        NOT NULL,
    tenant_id             uuid        NOT NULL,
    sync_version          bigint      NOT NULL,
    sync_changed_at       timestamptz NOT NULL,
    eliminado             boolean     NOT NULL DEFAULT false,
    pendiente_desde       timestamptz,
    encolado_en           timestamptz,
    CONSTRAINT pk_flito_sync_signals PRIMARY KEY (procedure_instance_id),
    -- CASCADE: un aviso es una cola técnica; no debe impedir el borrado físico de una compañía (sin FK al trámite,
    -- el aviso de un trámite borrado seguiría ahí).
    CONSTRAINT fk_flito_sync_signals_tenants FOREIGN KEY (tenant_id)
        REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_flito_sync_signals_tenant
    ON integrations.flito_sync_signals (tenant_id);

-- Cola del despachador: solo los pendientes, en el orden en que maduran (ventana de estabilidad del feed).
CREATE INDEX IF NOT EXISTS ix_flito_sync_signals_pendientes
    ON integrations.flito_sync_signals (sync_changed_at)
    WHERE pendiente_desde IS NOT NULL;

COMMENT ON TABLE integrations.flito_sync_signals IS
    'Avisos pendientes de enviar a FLITO, uno por trámite (HU #13622). Los registra trg_procedure_sync_stamp en la misma transacción del cambio; los despacha la HU #13624. Sin FK al trámite (DDL 128) ni RLS (ADR-0069).';
COMMENT ON COLUMN integrations.flito_sync_signals.sync_version IS
    'sync_version vigente del trámite al registrar el aviso. Coalescente: siempre la más reciente.';
COMMENT ON COLUMN integrations.flito_sync_signals.eliminado IS
    'true cuando el trámite tiene deleted_at (tombstone del feed v3.1).';
COMMENT ON COLUMN integrations.flito_sync_signals.pendiente_desde IS
    'Desde cuándo hay un aviso sin despachar. NULL = nada pendiente.';
COMMENT ON COLUMN integrations.flito_sync_signals.encolado_en IS
    'Último despacho: momento en que se dejó el webhook en la outbox del SDK (HU #13624).';

-- ─────────────────────────────────────────────────────────────────────────────
-- Registro del aviso (UPSERT coalescente)
-- ─────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION integrations.fn_flito_signal_registrar(n tramites.procedure_instances) RETURNS void AS $$
BEGIN
    IF n.is_migrated THEN
        -- Nunca entran al feed; si alguna vez se marcan como migrados, se retira el aviso pendiente.
        DELETE FROM integrations.flito_sync_signals WHERE procedure_instance_id = n.id;
        RETURN;
    END IF;

    INSERT INTO integrations.flito_sync_signals AS s
           (procedure_instance_id, tenant_id, sync_version, sync_changed_at, eliminado, pendiente_desde)
    VALUES (n.id, n.tenant_id, n.sync_version, n.sync_changed_at, n.deleted_at IS NOT NULL, now())
    ON CONFLICT (procedure_instance_id) DO UPDATE
       SET tenant_id       = EXCLUDED.tenant_id,
           sync_version    = EXCLUDED.sync_version,
           sync_changed_at = EXCLUDED.sync_changed_at,
           eliminado       = EXCLUDED.eliminado,
           pendiente_desde = COALESCE(s.pendiente_desde, EXCLUDED.pendiente_desde);
END;
$$ LANGUAGE plpgsql;

-- Mismo sello que el DDL 123, más el registro del aviso. En la rama de «ya sellado en esta transacción» solo se
-- vuelve a registrar si cambian el borrado lógico o la marca de migrado, para que el tombstone no quede viejo.
CREATE OR REPLACE FUNCTION tramites.trg_procedure_sync_stamp() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'UPDATE' AND OLD.sync_xact = pg_current_xact_id() THEN
        NEW.sync_version    := OLD.sync_version;
        NEW.sync_changed_at := OLD.sync_changed_at;
        NEW.sync_xact       := OLD.sync_xact;
        IF NEW.deleted_at IS DISTINCT FROM OLD.deleted_at OR NEW.is_migrated IS DISTINCT FROM OLD.is_migrated THEN
            PERFORM integrations.fn_flito_signal_registrar(NEW);
        END IF;
    ELSE
        NEW.sync_version    := nextval('tramites.procedure_sync_seq');
        NEW.sync_changed_at := now();
        NEW.sync_xact       := pg_current_xact_id();
        PERFORM integrations.fn_flito_signal_registrar(NEW);
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
