-- ADR-0050 (parte 2 de 2) — reset de expedientes y eliminación de los vocabularios paralelos.
-- Migración: PENDIENTE — se crea al cerrar la HU-02 (backend sin modalidad_entrada/tipologia_codigo).
--
-- ⚠️ DESTRUCTIVO Y BREAKING. NO existe todavía una clase Migration que lo ejecute, y es deliberado:
--    hoy 262 archivos de la solución siguen leyendo modalidad_entrada / tipologia_codigo, y
--    Database:AutoMigrate está en true por defecto (Flit.Api/Program.cs), así que registrar esta
--    migración antes de tiempo rompería el arranque de la aplicación.
--
--    Activar SOLO cuando: (a) HU-02 haya eliminado esas columnas del modelo EF y de todos sus
--    consumidores, y (b) exista respaldo verificado y aprobación explícita del borrado.
--
-- Idempotente: el borrado va guardado por la existencia de modalidad_entrada, de modo que una
-- reaplicación posterior no vuelve a borrar datos.

-- 1. Reset de expedientes (guardado: solo mientras exista el modelo viejo)
-- ============================================================================
-- Se usa DELETE y no TRUNCATE ... CASCADE deliberadamente: hay FKs con ON DELETE SET NULL
-- (admin.plate_range_details, tramites.external_query_cache, tramites.person_data_consents,
-- tramites.procedure_instance_biometric_validations) que TRUNCATE vaciaría por completo en
-- lugar de desasociar. DELETE respeta la política declarada en cada FK.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
         WHERE table_schema = 'tramites'
           AND table_name   = 'procedure_instances'
           AND column_name  = 'modalidad_entrada'
    ) THEN
        DELETE FROM tramites.procedure_instances;
        RAISE NOTICE 'ADR-0050: expedientes eliminados; la clasificación pasa a procedure_types.';
    END IF;
END $$;

-- ============================================================================
-- 2. procedure_instances — fuera los vocabularios paralelos
-- ============================================================================
ALTER TABLE tramites.procedure_instances
    DROP COLUMN IF EXISTS modalidad_entrada,
    DROP COLUMN IF EXISTS tipologia_codigo;

-- 3. Causales de rechazo — de modalidad ×2 a familia ×3
-- ============================================================================
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
         WHERE table_schema = 'catalogs'
           AND table_name   = 'rejection_reasons'
           AND column_name  = 'modalidad'
    ) THEN
        ALTER TABLE catalogs.rejection_reasons
            DROP CONSTRAINT IF EXISTS ck_rejection_reasons_modalidad;

        ALTER TABLE catalogs.rejection_reasons
            RENAME COLUMN modalidad TO family;

        UPDATE catalogs.rejection_reasons
           SET family = CASE upper(btrim(family))
                          WHEN 'MATRICULA_INICIAL' THEN 'MATRICULAS'
                          WHEN 'TRASPASO'          THEN 'TRASPASO'
                          ELSE 'OTROS'
                        END;

        ALTER INDEX IF EXISTS catalogs.ix_rejection_reasons_modalidad
            RENAME TO ix_rejection_reasons_family;
    END IF;
END $$;

ALTER TABLE catalogs.rejection_reasons
    DROP CONSTRAINT IF EXISTS ck_rejection_reasons_family;
ALTER TABLE catalogs.rejection_reasons
    ADD CONSTRAINT ck_rejection_reasons_family
        CHECK (family IN ('MATRICULAS', 'TRASPASO', 'OTROS'));

COMMENT ON COLUMN catalogs.rejection_reasons.family IS
'Familia del tipo de trámite a la que aplica la causal (ADR-0050). Valores de tramites.procedure_types.family.';
