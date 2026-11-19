-- =============================================================================
-- HU #14071 (Feature J, Épica #14021) — tarifas del portal de Persona Natural: modelo versionado con vigencia.
-- Migración: 20261027180000_HU14071_TarifasPersonaNatural.
--
-- Hallazgo previo: FLIT 2 no guarda ninguna tarifa por organismo ni por tipo (catalogs.transit_offices y
-- tramites.procedure_types no tienen columnas de valor). Esta tabla es la ÚNICA fuente de los valores de la cotización
-- del portal (HU #14043, RPC PersonaNaturalService.Cotizar). Siembra VACÍA: sin tarifa vigente la cotización responde
-- tarifa_no_vigente; no se inventa ningún precio.
--
-- Modelo: una fila = UNA versión de UN concepto para un ámbito.
--   concept            DERECHOS_TRANSITO | SERVICIO_FLIT | CAMBIO_COLOR | CONVERSION_COMBUSTIBLE | CAMBIO_CARROCERIA |
--                      ENVIO_DOCUMENTO.
--   procedure_type_id  tipo de trámite; NULL = el valor sirve para cualquier tipo (solo conceptos adicionales).
--   transit_office_id  organismo; NULL = valor GENERAL; con valor = EXCEPCIÓN de ese organismo (manda sobre el general).
--   Precedencia al calcular: (tipo, organismo) > (tipo, general) > (cualquier tipo, organismo) > (cualquier tipo, general).
--   amount_cop         entero en pesos colombianos >= 0 (bigint: sin decimales ni float, regla de montos del proyecto).
--   valid_from/valid_to vigencia en días inclusive, hora de Colombia; valid_to NULL = abierta.
--
-- Versionado (AC3): admin.pn_tariff_new_version(...) cierra la versión abierta anterior el día anterior al inicio de la
-- nueva e inserta la nueva en la misma transacción. El trigger impide dos versiones vigentes del mismo ámbito en la misma
-- fecha, impide borrar y deja inmutable toda versión cerrada (historial); en una abierta solo se puede cerrar valid_to.
-- Los cambios de tarifa no tocan lo ya cotizado: la solicitud del portal congela su cotización en el schema
-- persona_natural y el RPC devuelve el id de las versiones usadas (version_tarifa) para ese snapshot (AC4).
--
-- Tabla GLOBAL sin tenant_id: excepción análoga a admin.banners (ADR-0058) — las tarifas del portal las administra la
-- plataforma, no una compañía. Sin RLS por la misma razón. Sin soft delete: las versiones no se borran (historial).
-- Aditiva: no altera ninguna tabla existente. El permiso de administración (platform.persona_natural.manage) y los
-- endpoints llegan con el Feature J.
-- =============================================================================

CREATE TABLE admin.persona_natural_tariffs (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_persona_natural_tariffs PRIMARY KEY (id),
    concept varchar(40) NOT NULL,
    procedure_type_id uuid,
    transit_office_id uuid,
    amount_cop bigint NOT NULL,
    valid_from date NOT NULL,
    valid_to date,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT fk_persona_natural_tariffs_procedure_types FOREIGN KEY (procedure_type_id)
        REFERENCES tramites.procedure_types (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_persona_natural_tariffs_transit_offices FOREIGN KEY (transit_office_id)
        REFERENCES catalogs.transit_offices (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_persona_natural_tariffs_concept CHECK (concept IN (
        'DERECHOS_TRANSITO', 'SERVICIO_FLIT', 'CAMBIO_COLOR', 'CONVERSION_COMBUSTIBLE', 'CAMBIO_CARROCERIA',
        'ENVIO_DOCUMENTO')),
    CONSTRAINT ck_persona_natural_tariffs_amount CHECK (amount_cop >= 0),
    CONSTRAINT ck_persona_natural_tariffs_vigencia CHECK (valid_to IS NULL OR valid_to >= valid_from),
    -- Los dos renglones de la cotización siempre son «de un tipo»; solo los adicionales pueden valer para cualquiera.
    CONSTRAINT ck_persona_natural_tariffs_scope CHECK (
        procedure_type_id IS NOT NULL OR concept NOT IN ('DERECHOS_TRANSITO', 'SERVICIO_FLIT'))
);

CREATE INDEX ix_persona_natural_tariffs_procedure_type ON admin.persona_natural_tariffs (procedure_type_id);
CREATE INDEX ix_persona_natural_tariffs_transit_office ON admin.persona_natural_tariffs (transit_office_id);
-- «Tarifa vigente»: por concepto y fecha de inicio.
CREATE INDEX ix_persona_natural_tariffs_concept_vigencia ON admin.persona_natural_tariffs (concept, valid_from, valid_to);
-- Una misma versión (ámbito + inicio) no se repite; NULL del ámbito cuenta como un valor propio.
CREATE UNIQUE INDEX uq_persona_natural_tariffs_scope_start ON admin.persona_natural_tariffs (
    concept,
    COALESCE(procedure_type_id, '00000000-0000-0000-0000-000000000000'::uuid),
    COALESCE(transit_office_id, '00000000-0000-0000-0000-000000000000'::uuid),
    valid_from);

COMMENT ON TABLE admin.persona_natural_tariffs IS
    'Tarifas versionadas del portal de Persona Natural (HU #14071). Global sin tenant_id (analoga a admin.banners, ADR-0058). Sin PII.';

-- Guarda de versionado: sin solapes, sin borrado, historial inmutable.
CREATE OR REPLACE FUNCTION admin.trg_persona_natural_tariffs_guard() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Las versiones de tarifa no se eliminan: se cierran con una versión nueva.' USING ERRCODE = 'restrict_violation';
    END IF;

    IF TG_OP = 'UPDATE' THEN
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

    -- INSERT: serializa por ámbito y rechaza una vigencia que se cruce con otra versión del mismo ámbito.
    PERFORM pg_advisory_xact_lock(hashtextextended(
        NEW.concept || '|' || COALESCE(NEW.procedure_type_id::text, '-') || '|' || COALESCE(NEW.transit_office_id::text, '-'), 0));
    IF EXISTS (
        SELECT 1 FROM admin.persona_natural_tariffs t
         WHERE t.concept = NEW.concept
           AND t.procedure_type_id IS NOT DISTINCT FROM NEW.procedure_type_id
           AND t.transit_office_id IS NOT DISTINCT FROM NEW.transit_office_id
           AND t.id <> NEW.id
           AND t.valid_from <= COALESCE(NEW.valid_to, 'infinity'::date)
           AND COALESCE(t.valid_to, 'infinity'::date) >= NEW.valid_from) THEN
        RAISE EXCEPTION 'Ya existe una tarifa vigente para ese concepto, tipo y organismo en esas fechas.' USING ERRCODE = 'unique_violation';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_persona_natural_tariffs_guard ON admin.persona_natural_tariffs;
CREATE TRIGGER tr_persona_natural_tariffs_guard BEFORE INSERT OR UPDATE OR DELETE ON admin.persona_natural_tariffs
    FOR EACH ROW EXECUTE FUNCTION admin.trg_persona_natural_tariffs_guard();
DROP TRIGGER IF EXISTS tr_persona_natural_tariffs_row_version ON admin.persona_natural_tariffs;
CREATE TRIGGER tr_persona_natural_tariffs_row_version BEFORE UPDATE ON admin.persona_natural_tariffs
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_persona_natural_tariffs_audit ON admin.persona_natural_tariffs;
CREATE TRIGGER tr_persona_natural_tariffs_audit AFTER INSERT OR UPDATE OR DELETE ON admin.persona_natural_tariffs
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

-- Versión nueva: cierra la abierta anterior el día anterior al inicio y inserta la nueva (una sola transacción).
CREATE OR REPLACE FUNCTION admin.persona_natural_tariff_new_version(
    p_concept varchar, p_procedure_type_id uuid, p_transit_office_id uuid, p_amount_cop bigint, p_valid_from date, p_user uuid)
RETURNS uuid AS $$
DECLARE
    v_id uuid;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtextextended(
        p_concept || '|' || COALESCE(p_procedure_type_id::text, '-') || '|' || COALESCE(p_transit_office_id::text, '-'), 0));
    UPDATE admin.persona_natural_tariffs t
       SET valid_to = p_valid_from - 1, updated_by = p_user, updated_at = now()
     WHERE t.concept = p_concept
       AND t.procedure_type_id IS NOT DISTINCT FROM p_procedure_type_id
       AND t.transit_office_id IS NOT DISTINCT FROM p_transit_office_id
       AND t.valid_to IS NULL
       AND t.valid_from < p_valid_from;
    INSERT INTO admin.persona_natural_tariffs (concept, procedure_type_id, transit_office_id, amount_cop, valid_from, created_by)
    VALUES (p_concept, p_procedure_type_id, p_transit_office_id, p_amount_cop, p_valid_from, p_user)
    RETURNING id INTO v_id;
    RETURN v_id;
END;
$$ LANGUAGE plpgsql;
