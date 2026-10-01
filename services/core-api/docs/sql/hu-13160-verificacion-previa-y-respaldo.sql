-- HU #13160 (Feature #13120 F8, Épica #13090) — verificación previa y respaldo ANTES de la migración
-- 20261001140334_HU13160_LimpiezaIdentidadMandatario (DDL 127, DESTRUCTIVA). Solo lectura: no modifica nada.
--
-- Cómo usarlo, en cada ambiente (DEV, QA, PDN; en PDN solo con ventana de despliegue):
--   1. Correr este script con un rol que pueda leer las tablas (propietario o con row_security = off).
--   2. Pegar los conteos en la Discussion de la HU #13160 (AC3).
--   3. Si ALGÚN conteo es mayor que cero, NO desplegar: informar al PO y decidir qué hacer con esos datos (AC5).
--      La migración también lo comprueba y aborta sin borrar nada.
--   4. Hacer el respaldo de las tres estructuras (comandos al final) y solo entonces aplicar la migración.
--
-- NO cuenta admin.company_legal_representatives.identity_validation_ref: esa columna se conserva (la usa el representante legal).

SET row_security = off;

SELECT 'admin.admin_identity_validations (filas)' AS estructura,
       CASE WHEN to_regclass('admin.admin_identity_validations') IS NULL THEN NULL
            ELSE (xpath('/row/c/text()',
                        query_to_xml('SELECT count(*) AS c FROM admin.admin_identity_validations', false, true, '')))[1]::text::bigint
       END AS conteo
UNION ALL
SELECT 'admin.mandate_signers.identity_validation_ref (no nulos)',
       CASE WHEN EXISTS (SELECT 1 FROM information_schema.columns
                          WHERE table_schema = 'admin' AND table_name = 'mandate_signers' AND column_name = 'identity_validation_ref')
            THEN (xpath('/row/c/text()',
                        query_to_xml('SELECT count(*) AS c FROM admin.mandate_signers WHERE identity_validation_ref IS NOT NULL', false, true, '')))[1]::text::bigint
       END
UNION ALL
SELECT 'admin.transit_office_mandate_config.custom_field_manifest (no nulos)',
       CASE WHEN EXISTS (SELECT 1 FROM information_schema.columns
                          WHERE table_schema = 'admin' AND table_name = 'transit_office_mandate_config' AND column_name = 'custom_field_manifest')
            THEN (xpath('/row/c/text()',
                        query_to_xml('SELECT count(*) AS c FROM admin.transit_office_mandate_config WHERE custom_field_manifest IS NOT NULL', false, true, '')))[1]::text::bigint
       END;

-- Respaldo (ejecutar desde la terminal del servidor, NO desde este script). Ajustar host, usuario y base:
--   pg_dump -h <host> -U <usuario> -d <base> -t admin.admin_identity_validations -Fc -f hu13160-admin_identity_validations.dump
--   pg_dump -h <host> -U <usuario> -d <base> -t admin.mandate_signers -Fc -f hu13160-mandate_signers.dump
--   pg_dump -h <host> -U <usuario> -d <base> -t admin.transit_office_mandate_config -Fc -f hu13160-transit_office_mandate_config.dump
-- Dejar en la Discussion los tres nombres de archivo y los conteos de arriba.
