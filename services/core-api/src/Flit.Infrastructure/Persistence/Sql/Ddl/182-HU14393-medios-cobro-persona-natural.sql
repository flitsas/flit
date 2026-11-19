-- =============================================================================
-- HU #14393 (Feature #14392, Épica #14297) — medios de pago y switches de cobro del portal de Persona Natural.
-- Migración: 20261106100000_HU14393_MediosCobroPersonaNatural. Idempotente y aditiva: no altera ninguna tabla existente.
--
-- Ámbito: transit_office_id. NULL = la web de persona; con valor = el portal del punto de ese organismo.
--
--   admin.persona_natural_payment_methods  bandera por medio (PASARELA | PIN) y ámbito. Sin fila: PASARELA encendida y PIN
--                                          apagada (hoy el portal solo cobra por pasarela). PIN no existe en la web.
--   admin.persona_natural_charge_switches  switch por ámbito, tipo de trámite, medio y concepto de cobro:
--                                          TRAMITE (derechos de tránsito y transformaciones) | GESTION (servicio FLIT y envío).
--                                          Sin fila el switch vale encendido: una instalación sin configurar cobra como hoy.
--
-- El monto NO vive aquí: sigue en admin.persona_natural_tariffs. Apagar un switch conserva el monto. Encender uno exige
-- monto mayor que cero (regla de la aplicación, que lee la tarifa vigente). Tablas globales sin tenant_id, como las tarifas.
-- =============================================================================

CREATE TABLE IF NOT EXISTS admin.persona_natural_payment_methods (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_persona_natural_payment_methods PRIMARY KEY (id),
    transit_office_id uuid,
    method varchar(20) NOT NULL,
    enabled boolean NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT fk_persona_natural_payment_methods_transit_offices FOREIGN KEY (transit_office_id)
        REFERENCES catalogs.transit_offices (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_persona_natural_payment_methods_method CHECK (method IN ('PASARELA', 'PIN')),
    -- El PIN de caja es del punto del organismo: la web no lo tiene.
    CONSTRAINT ck_persona_natural_payment_methods_pin_web CHECK (transit_office_id IS NOT NULL OR method <> 'PIN')
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_persona_natural_payment_methods_scope ON admin.persona_natural_payment_methods (
    method, COALESCE(transit_office_id, '00000000-0000-0000-0000-000000000000'::uuid));

CREATE TABLE IF NOT EXISTS admin.persona_natural_charge_switches (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_persona_natural_charge_switches PRIMARY KEY (id),
    transit_office_id uuid,
    procedure_type_id uuid NOT NULL,
    method varchar(20) NOT NULL,
    charge varchar(20) NOT NULL,
    enabled boolean NOT NULL,
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT fk_persona_natural_charge_switches_transit_offices FOREIGN KEY (transit_office_id)
        REFERENCES catalogs.transit_offices (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT fk_persona_natural_charge_switches_procedure_types FOREIGN KEY (procedure_type_id)
        REFERENCES tramites.procedure_types (id) ON DELETE RESTRICT ON UPDATE RESTRICT,
    CONSTRAINT ck_persona_natural_charge_switches_method CHECK (method IN ('PASARELA', 'PIN')),
    CONSTRAINT ck_persona_natural_charge_switches_charge CHECK (charge IN ('TRAMITE', 'GESTION')),
    CONSTRAINT ck_persona_natural_charge_switches_pin_web CHECK (transit_office_id IS NOT NULL OR method <> 'PIN')
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_persona_natural_charge_switches_scope ON admin.persona_natural_charge_switches (
    COALESCE(transit_office_id, '00000000-0000-0000-0000-000000000000'::uuid), procedure_type_id, method, charge);

COMMENT ON TABLE admin.persona_natural_payment_methods IS
    'Bandera por medio de pago (PASARELA, PIN) y ámbito del portal de Persona Natural (HU #14393). transit_office_id NULL = web. Global sin tenant_id. Sin PII.';
COMMENT ON TABLE admin.persona_natural_charge_switches IS
    'Switch por ámbito, tipo, medio y concepto de cobro (TRAMITE, GESTION) del portal de Persona Natural (HU #14393). Sin fila = encendido. El monto vive en persona_natural_tariffs. Sin PII.';

DROP TRIGGER IF EXISTS tr_persona_natural_payment_methods_row_version ON admin.persona_natural_payment_methods;
CREATE TRIGGER tr_persona_natural_payment_methods_row_version BEFORE UPDATE ON admin.persona_natural_payment_methods
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_persona_natural_payment_methods_audit ON admin.persona_natural_payment_methods;
CREATE TRIGGER tr_persona_natural_payment_methods_audit AFTER INSERT OR UPDATE OR DELETE ON admin.persona_natural_payment_methods
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();

DROP TRIGGER IF EXISTS tr_persona_natural_charge_switches_row_version ON admin.persona_natural_charge_switches;
CREATE TRIGGER tr_persona_natural_charge_switches_row_version BEFORE UPDATE ON admin.persona_natural_charge_switches
    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_persona_natural_charge_switches_audit ON admin.persona_natural_charge_switches;
CREATE TRIGGER tr_persona_natural_charge_switches_audit AFTER INSERT OR UPDATE OR DELETE ON admin.persona_natural_charge_switches
    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
