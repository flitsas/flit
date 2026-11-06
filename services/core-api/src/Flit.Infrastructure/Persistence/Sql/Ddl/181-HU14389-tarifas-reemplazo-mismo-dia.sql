-- =============================================================================
-- HU #14389 (Feature #14387, Épica #14297) — corregir un precio el mismo día sin error de tarifa solapada.
-- Migración: 20261105130000_HU14389_TarifasReemplazoMismoDia. Idempotente.
--
-- Hasta ahora, registrar un valor con el mismo inicio que una versión existente chocaba con el índice único
-- uq_persona_natural_tariffs_scope_start y con el trigger de solapes (tarifa_solapada). Ahora el valor nuevo REEMPLAZA a la
-- versión que empezó ese día.
--
-- Mecanismo: la fila reemplazada NO se borra (una cotización pudo congelar su id en version_tarifa, y el trigger prohíbe el
-- DELETE). Se marca con superseded_at (columna NO mapeada en EF: la tabla no es entidad del DbContext y el modelo no cambia):
--   * El índice único pasa a ser PARCIAL (WHERE superseded_at IS NULL): una sola versión viva por ámbito y fecha de inicio.
--   * El trigger ignora las reemplazadas en el chequeo de solapes y las deja inmutables (historial). Lo único que permite
--     sobre una versión viva es marcarla (superseded_at NULL -> fecha) sin tocar nada más, incluso si estaba cerrada.
--   * Toda lectura de «versión vigente» o de historial excluye superseded_at IS NOT NULL (repositorios y lector del portal).
--     Las lecturas por id (cotizaciones congeladas) siguen encontrando la fila.
-- La auditoría (trigger trg_audit_log + auditoría de aplicación) conserva el valor reemplazado y el nuevo con autor y hora.
-- Aditiva: no altera datos existentes (superseded_at nace NULL en todas las filas).
-- =============================================================================

ALTER TABLE admin.persona_natural_tariffs ADD COLUMN IF NOT EXISTS superseded_at timestamptz;

COMMENT ON COLUMN admin.persona_natural_tariffs.superseded_at IS
    'HU #14389: la versión fue reemplazada por otra con el mismo inicio (corrección el mismo día). No se borra: una cotización pudo congelar su id. Las lecturas vigentes y el historial la excluyen.';

DROP INDEX IF EXISTS admin.uq_persona_natural_tariffs_scope_start;
CREATE UNIQUE INDEX IF NOT EXISTS uq_persona_natural_tariffs_scope_start ON admin.persona_natural_tariffs (
    concept,
    COALESCE(procedure_type_id, '00000000-0000-0000-0000-000000000000'::uuid),
    COALESCE(transit_office_id, '00000000-0000-0000-0000-000000000000'::uuid),
    valid_from)
    WHERE superseded_at IS NULL;

-- Guarda de versionado: sin solapes entre versiones vivas, sin borrado, historial (cerrado o reemplazado) inmutable.
CREATE OR REPLACE FUNCTION admin.trg_persona_natural_tariffs_guard() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Las versiones de tarifa no se eliminan: se cierran con una versión nueva.' USING ERRCODE = 'restrict_violation';
    END IF;

    IF TG_OP = 'UPDATE' THEN
        IF OLD.superseded_at IS NOT NULL THEN
            RAISE EXCEPTION 'Una versión de tarifa reemplazada es historial y no se edita.' USING ERRCODE = 'restrict_violation';
        END IF;
        IF NEW.superseded_at IS DISTINCT FROM OLD.superseded_at THEN
            -- Reemplazo: solo se marca la versión; ningún otro dato cambia.
            IF NEW.concept <> OLD.concept
               OR NEW.procedure_type_id IS DISTINCT FROM OLD.procedure_type_id
               OR NEW.transit_office_id IS DISTINCT FROM OLD.transit_office_id
               OR NEW.amount_cop <> OLD.amount_cop
               OR NEW.valid_from <> OLD.valid_from
               OR NEW.valid_to IS DISTINCT FROM OLD.valid_to THEN
                RAISE EXCEPTION 'Al reemplazar una versión de tarifa solo se marca como reemplazada; sus datos no cambian.' USING ERRCODE = 'restrict_violation';
            END IF;
            RETURN NEW;
        END IF;
        IF OLD.valid_to IS NOT NULL THEN
            RAISE EXCEPTION 'Una versión de tarifa cerrada es historial y no se edita.' USING ERRCODE = 'restrict_violation';
        END IF;
        IF NEW.concept <> OLD.concept
           OR NEW.procedure_type_id IS DISTINCT FROM OLD.procedure_type_id
           OR NEW.transit_office_id IS DISTINCT FROM OLD.transit_office_id
           OR NEW.amount_cop <> OLD.amount_cop
           OR NEW.valid_from <> OLD.valid_from THEN
            RAISE EXCEPTION 'De una versión de tarifa abierta solo se puede cerrar la vigencia; el valor cambia con una versión nueva.' USING ERRCODE = 'restrict_violation';
        END IF;
        RETURN NEW;
    END IF;

    -- INSERT: serializa por ámbito y rechaza una vigencia que se cruce con otra versión VIVA del mismo ámbito.
    PERFORM pg_advisory_xact_lock(hashtextextended(
        NEW.concept || '|' || COALESCE(NEW.procedure_type_id::text, '-') || '|' || COALESCE(NEW.transit_office_id::text, '-'), 0));
    IF EXISTS (
        SELECT 1 FROM admin.persona_natural_tariffs t
         WHERE t.concept = NEW.concept
           AND t.procedure_type_id IS NOT DISTINCT FROM NEW.procedure_type_id
           AND t.transit_office_id IS NOT DISTINCT FROM NEW.transit_office_id
           AND t.id <> NEW.id
           AND t.superseded_at IS NULL
           AND t.valid_from <= COALESCE(NEW.valid_to, 'infinity'::date)
           AND COALESCE(t.valid_to, 'infinity'::date) >= NEW.valid_from) THEN
        RAISE EXCEPTION 'Ya existe una tarifa vigente para ese concepto, tipo y organismo en esas fechas.' USING ERRCODE = 'unique_violation';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Versión nueva: reemplaza la versión viva que empezó el mismo día, cierra la abierta anterior el día anterior al inicio e
-- inserta la nueva (una sola transacción). Devuelve el id de la versión nueva.
CREATE OR REPLACE FUNCTION admin.persona_natural_tariff_new_version(
    p_concept varchar, p_procedure_type_id uuid, p_transit_office_id uuid, p_amount_cop bigint, p_valid_from date, p_user uuid)
RETURNS uuid AS $$
DECLARE
    v_id uuid;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended(
        p_concept || '|' || COALESCE(p_procedure_type_id::text, '-') || '|' || COALESCE(p_transit_office_id::text, '-'), 0));
    -- Mismo día: la versión viva con el mismo inicio queda reemplazada (no se borra).
    UPDATE admin.persona_natural_tariffs t
       SET superseded_at = now(), updated_by = p_user, updated_at = now()
     WHERE t.concept = p_concept
       AND t.procedure_type_id IS NOT DISTINCT FROM p_procedure_type_id
       AND t.transit_office_id IS NOT DISTINCT FROM p_transit_office_id
       AND t.superseded_at IS NULL
       AND t.valid_from = p_valid_from;
    UPDATE admin.persona_natural_tariffs t
       SET valid_to = p_valid_from - 1, updated_by = p_user, updated_at = now()
     WHERE t.concept = p_concept
       AND t.procedure_type_id IS NOT DISTINCT FROM p_procedure_type_id
       AND t.transit_office_id IS NOT DISTINCT FROM p_transit_office_id
       AND t.superseded_at IS NULL
       AND t.valid_to IS NULL
       AND t.valid_from < p_valid_from;
    INSERT INTO admin.persona_natural_tariffs (concept, procedure_type_id, transit_office_id, amount_cop, valid_from, created_by)
    VALUES (p_concept, p_procedure_type_id, p_transit_office_id, p_amount_cop, p_valid_from, p_user)
    RETURNING id INTO v_id;
    RETURN v_id;
END;
$$ LANGUAGE plpgsql;
