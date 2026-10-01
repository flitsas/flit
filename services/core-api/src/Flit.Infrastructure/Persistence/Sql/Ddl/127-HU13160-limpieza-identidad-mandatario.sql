-- HU #13160 (Feature #13120 F8, Épica #13090 Mandatarios) — limpieza del modelo de mandatarios. Migración: 20261001140334_HU13160_LimpiezaIdentidadMandatario.
--
-- DESTRUCTIVA (DROP irreversible en datos). Qué elimina, por ser estructuras sin uso desde ADR-0050 (la vigencia de la
-- identidad sale del módulo Identidad: tramites.procedure_instance_biometric_validations):
--   1. Tabla admin.admin_identity_validations, con sus 3 índices, los triggers tr_admin_identity_validations_row_version y
--      tr_admin_identity_validations_audit y la política RLS tenant_isolation (el DROP TABLE los arrastra).
--   2. Columna admin.mandate_signers.identity_validation_ref.
--   3. Columna admin.transit_office_mandate_config.custom_field_manifest.
-- NO toca admin.company_legal_representatives.identity_validation_ref: es otra columna con el mismo nombre y el representante
-- legal la usa activamente (RepresentanteLegalIdentityUpdater, LegalRepresentativeRepository).
--
-- GUARDA DE DATOS (AC5): si la tabla tiene filas, o alguna de las dos columnas tiene valores no nulos, la migración ABORTA
-- sin borrar nada: hay que informar al PO y decidir qué hacer con esos datos antes de volver a correrla. Se cuenta con
-- row_security = off para que la RLS de la tabla (tenant_isolation) no oculte filas y dé un falso cero.
-- RESPALDO PREVIO (AC3): antes de aplicar en un ambiente con datos, correr docs/sql/hu-13160-verificacion-previa-y-respaldo.sql
-- (conteos de solo lectura + instrucciones de pg_dump de las tres estructuras) y dejar los conteos en la Discussion de la HU.
-- Idempotente: DROP ... IF EXISTS. Reversa (Down de la migración): recrea la estructura SIN datos reutilizando los DDL 40 y 74.

DO $mig$
DECLARE
    v_tabla    bigint := 0;
    v_ref      bigint := 0;
    v_manifest bigint := 0;
BEGIN
    PERFORM set_config('row_security', 'off', true);

    IF to_regclass('admin.admin_identity_validations') IS NOT NULL THEN
        EXECUTE 'SELECT count(*) FROM admin.admin_identity_validations' INTO v_tabla;
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'admin' AND table_name = 'mandate_signers'
                  AND column_name = 'identity_validation_ref') THEN
        EXECUTE 'SELECT count(*) FROM admin.mandate_signers WHERE identity_validation_ref IS NOT NULL' INTO v_ref;
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'admin' AND table_name = 'transit_office_mandate_config'
                  AND column_name = 'custom_field_manifest') THEN
        EXECUTE 'SELECT count(*) FROM admin.transit_office_mandate_config WHERE custom_field_manifest IS NOT NULL' INTO v_manifest;
    END IF;

    IF v_tabla > 0 OR v_ref > 0 OR v_manifest > 0 THEN
        RAISE EXCEPTION 'HU #13160: no se aplica el DROP, hay datos en uso (admin_identity_validations=%, mandate_signers.identity_validation_ref=%, transit_office_mandate_config.custom_field_manifest=%). Informar al PO y decidir antes de continuar.',
            v_tabla, v_ref, v_manifest;
    END IF;
END
$mig$;

DROP TABLE IF EXISTS admin.admin_identity_validations CASCADE;
ALTER TABLE admin.mandate_signers DROP COLUMN IF EXISTS identity_validation_ref;
ALTER TABLE admin.transit_office_mandate_config DROP COLUMN IF EXISTS custom_field_manifest;
