-- HU #13128 (Feature #13114 F2, Épica #13090 Mandatarios) — ADR-0061. Migración: 20260930120000_HU13128_MandatarioModeloVigenciaOrigen.
--
-- Qué agrega (aditivo, sin tablas nuevas, sin RLS nuevo; signs_physically no se toca):
--   admin.mandate_signers                  signer_model, signature_method, validity_kind, valid_from, valid_to,
--                                          deleted_at, deleted_by (baja lógica «Eliminar», distinta de is_active);
--                                          document_number pasa a NULL-able (solo formato_blanco, AC8).
--   admin.company_ot_mandate_rules         configured_by_scope (origen de la configuración)
--   admin.transit_office_mandate_config    configured_by_scope
--   admin.mandate_signer_companies         configured_by_scope (ampliación AC7, ADR-0061 D-7)
--
-- Backfill (solo cuando la columna se crea en esta ejecución; re-ejecutar no pisa datos posteriores):
--   signer_model='natural', validity_kind='fixed', sin fechas.
--   signature_method: 'baul' si hay signature_vault_id; si no, 'biometria' si hay identity_validation_ref o
--   una validación biométrica APROBADA del documento en el tenant de alguna compañía vinculada activa
--   (criterio de MandateSignerIdentityTenantResolver: sin compañía activa, el tenant propio del OT); nulo si nada.
--   configured_by_scope (reglas y config): 'super_admin' si el último autor (updated_by, si no created_by) es
--   Super Admin; 'organismo' en los demás casos, incluidos autores no resolubles. Nunca nulo.
--   configured_by_scope (vínculos): por created_by del mandatario: Super Admin -> super_admin; usuario de un
--   tenant de OT -> organismo; usuario de otro tenant (compañía) -> compania; sin resolver -> organismo.
-- Idempotente (IF NOT EXISTS / guardas en pg_constraint). Reversible: ver Down en la migración.

DO $mig$
DECLARE
    v_new_method   boolean := NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'admin' AND table_name = 'mandate_signers' AND column_name = 'signature_method');
    v_new_rules    boolean := NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'admin' AND table_name = 'company_ot_mandate_rules' AND column_name = 'configured_by_scope');
    v_new_config   boolean := NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'admin' AND table_name = 'transit_office_mandate_config' AND column_name = 'configured_by_scope');
    v_new_links    boolean := NOT EXISTS (SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'admin' AND table_name = 'mandate_signer_companies' AND column_name = 'configured_by_scope');
BEGIN
    -- ── admin.mandate_signers ───────────────────────────────────────────────────────────────────────
    ALTER TABLE admin.mandate_signers
        ADD COLUMN IF NOT EXISTS signer_model     varchar(20)  NOT NULL DEFAULT 'natural',
        ADD COLUMN IF NOT EXISTS signature_method varchar(20)  NULL,
        ADD COLUMN IF NOT EXISTS validity_kind    varchar(10)  NOT NULL DEFAULT 'fixed',
        ADD COLUMN IF NOT EXISTS valid_from       date         NULL,
        ADD COLUMN IF NOT EXISTS valid_to         date         NULL,
        ADD COLUMN IF NOT EXISTS deleted_at       timestamptz  NULL,
        ADD COLUMN IF NOT EXISTS deleted_by       uuid         NULL;

    -- AC8: el número de documento es opcional solo para formato_blanco (lo exige el CHECK de abajo).
    ALTER TABLE admin.mandate_signers ALTER COLUMN document_number DROP NOT NULL;

    IF v_new_method THEN
        -- AC1/AC9. Si hay baúl gana 'baul' aunque exista biometría.
        UPDATE admin.mandate_signers ms
           SET signature_method = CASE
                   WHEN ms.signature_vault_id IS NOT NULL THEN 'baul'
                   ELSE 'biometria'
               END
         WHERE ms.signature_vault_id IS NOT NULL
            OR ms.identity_validation_ref IS NOT NULL
            OR EXISTS (
                SELECT 1
                  FROM tramites.procedure_instance_biometric_validations v
                 WHERE v.status = 'aprobado'
                   AND v.deleted_at IS NULL
                   AND upper(btrim(v.document_type))   = upper(btrim(ms.document_type))
                   AND upper(btrim(v.document_number)) = upper(btrim(ms.document_number))
                   AND v.tenant_id IN (
                       SELECT c.company_tenant_id
                         FROM admin.mandate_signer_companies c
                        WHERE c.mandate_signer_id = ms.id AND c.is_active
                       UNION ALL
                       SELECT p.tenant_id
                         FROM admin.transit_office_profiles p
                        WHERE p.transit_office_id = ms.transit_office_id
                          AND NOT EXISTS (SELECT 1 FROM admin.mandate_signer_companies c2
                                           WHERE c2.mandate_signer_id = ms.id AND c2.is_active)
                   )
            );
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_mandate_signers_signer_model'
                      AND conrelid = 'admin.mandate_signers'::regclass) THEN
        ALTER TABLE admin.mandate_signers ADD CONSTRAINT ck_mandate_signers_signer_model
            CHECK (signer_model IN ('natural', 'juridica', 'formato_blanco'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_mandate_signers_signature_method'
                      AND conrelid = 'admin.mandate_signers'::regclass) THEN
        ALTER TABLE admin.mandate_signers ADD CONSTRAINT ck_mandate_signers_signature_method
            CHECK (signature_method IS NULL OR signature_method IN ('baul', 'biometria'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_mandate_signers_validity_kind'
                      AND conrelid = 'admin.mandate_signers'::regclass) THEN
        ALTER TABLE admin.mandate_signers ADD CONSTRAINT ck_mandate_signers_validity_kind
            CHECK (validity_kind IN ('fixed', 'range'));
    END IF;
    -- AC5: coherencia de fechas.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_mandate_signers_validity_range'
                      AND conrelid = 'admin.mandate_signers'::regclass) THEN
        ALTER TABLE admin.mandate_signers ADD CONSTRAINT ck_mandate_signers_validity_range
            CHECK (validity_kind <> 'range'
                   OR (valid_from IS NOT NULL AND valid_to IS NOT NULL AND valid_to >= valid_from));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_mandate_signers_validity_fixed'
                      AND conrelid = 'admin.mandate_signers'::regclass) THEN
        ALTER TABLE admin.mandate_signers ADD CONSTRAINT ck_mandate_signers_validity_fixed
            CHECK (validity_kind <> 'fixed' OR (valid_from IS NULL AND valid_to IS NULL));
    END IF;
    -- juridica y formato_blanco: sin forma de firma ni vigencia por rango.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_mandate_signers_model_coherence'
                      AND conrelid = 'admin.mandate_signers'::regclass) THEN
        ALTER TABLE admin.mandate_signers ADD CONSTRAINT ck_mandate_signers_model_coherence
            CHECK (signer_model = 'natural'
                   OR (signature_method IS NULL AND validity_kind = 'fixed'
                       AND valid_from IS NULL AND valid_to IS NULL));
    END IF;
    -- AC8: documento obligatorio salvo formato_blanco.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_mandate_signers_document_required'
                      AND conrelid = 'admin.mandate_signers'::regclass) THEN
        ALTER TABLE admin.mandate_signers ADD CONSTRAINT ck_mandate_signers_document_required
            CHECK (signer_model = 'formato_blanco' OR document_number IS NOT NULL);
    END IF;

    -- ── configured_by_scope ─────────────────────────────────────────────────────────────────────────
    ALTER TABLE admin.company_ot_mandate_rules
        ADD COLUMN IF NOT EXISTS configured_by_scope varchar(20) NOT NULL DEFAULT 'organismo';
    ALTER TABLE admin.transit_office_mandate_config
        ADD COLUMN IF NOT EXISTS configured_by_scope varchar(20) NOT NULL DEFAULT 'organismo';
    ALTER TABLE admin.mandate_signer_companies
        ADD COLUMN IF NOT EXISTS configured_by_scope varchar(20) NOT NULL DEFAULT 'organismo';

    IF v_new_rules THEN
        UPDATE admin.company_ot_mandate_rules r
           SET configured_by_scope = 'super_admin'
         WHERE COALESCE(r.updated_by, r.created_by) IN (
               SELECT a.user_id FROM security.user_role_assignments a
                 JOIN security.roles ro ON ro.id = a.role_id
                WHERE ro.code = 'SuperAdmin' AND a.deleted_at IS NULL);
    END IF;
    IF v_new_config THEN
        UPDATE admin.transit_office_mandate_config t
           SET configured_by_scope = 'super_admin'
         WHERE COALESCE(t.updated_by, t.created_by) IN (
               SELECT a.user_id FROM security.user_role_assignments a
                 JOIN security.roles ro ON ro.id = a.role_id
                WHERE ro.code = 'SuperAdmin' AND a.deleted_at IS NULL);
    END IF;
    IF v_new_links THEN
        -- Origen = quien creó al mandatario.
        UPDATE admin.mandate_signer_companies c
           SET configured_by_scope = CASE
                   WHEN EXISTS (SELECT 1 FROM security.user_role_assignments a
                                  JOIN security.roles ro ON ro.id = a.role_id
                                 WHERE a.user_id = ms.created_by AND a.deleted_at IS NULL
                                   AND ro.code = 'SuperAdmin') THEN 'super_admin'
                   WHEN EXISTS (SELECT 1 FROM security.user_role_assignments a
                                  JOIN admin.transit_office_profiles p ON p.tenant_id = a.tenant_id
                                 WHERE a.user_id = ms.created_by AND a.deleted_at IS NULL) THEN 'organismo'
                   WHEN EXISTS (SELECT 1 FROM security.user_role_assignments a
                                 WHERE a.user_id = ms.created_by AND a.deleted_at IS NULL) THEN 'compania'
                   ELSE 'organismo'
               END
          FROM admin.mandate_signers ms
         WHERE ms.id = c.mandate_signer_id AND ms.created_by IS NOT NULL;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_company_ot_mandate_rules_configured_by_scope'
                      AND conrelid = 'admin.company_ot_mandate_rules'::regclass) THEN
        ALTER TABLE admin.company_ot_mandate_rules ADD CONSTRAINT ck_company_ot_mandate_rules_configured_by_scope
            CHECK (configured_by_scope IN ('organismo', 'compania', 'super_admin'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_transit_office_mandate_config_configured_by_scope'
                      AND conrelid = 'admin.transit_office_mandate_config'::regclass) THEN
        ALTER TABLE admin.transit_office_mandate_config ADD CONSTRAINT ck_transit_office_mandate_config_configured_by_scope
            CHECK (configured_by_scope IN ('organismo', 'compania', 'super_admin'));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_mandate_signer_companies_configured_by_scope'
                      AND conrelid = 'admin.mandate_signer_companies'::regclass) THEN
        ALTER TABLE admin.mandate_signer_companies ADD CONSTRAINT ck_mandate_signer_companies_configured_by_scope
            CHECK (configured_by_scope IN ('organismo', 'compania', 'super_admin'));
    END IF;
END
$mig$;

-- Índice parcial para listas y selectores (mandatarios no eliminados).
CREATE INDEX IF NOT EXISTS ix_mandate_signers_alive
    ON admin.mandate_signers (transit_office_id, is_active)
    WHERE deleted_at IS NULL;

COMMENT ON COLUMN admin.mandate_signers.signer_model IS 'ADR-0061 · Modelo del mandatario: natural | juridica | formato_blanco. Distinto de assignment_mode (tipo de mandato).';
COMMENT ON COLUMN admin.mandate_signers.signature_method IS 'ADR-0061 · Forma de firma: baul | biometria; nulo permitido (legados, juridica, formato_blanco). La obligatoriedad para natural la exige la API.';
COMMENT ON COLUMN admin.mandate_signers.validity_kind IS 'ADR-0061 · Vigencia propia: fixed (sin fechas) | range (valid_from y valid_to). Convive con los 30 días de la biometría.';
COMMENT ON COLUMN admin.mandate_signers.valid_from IS 'ADR-0061 · Inicio de la vigencia por rango (America/Bogota, date).';
COMMENT ON COLUMN admin.mandate_signers.valid_to IS 'ADR-0061 · Fin de la vigencia por rango (America/Bogota, date).';
COMMENT ON COLUMN admin.mandate_signers.deleted_at IS 'ADR-0061 · Baja lógica de «Eliminar» (oculta sin borrar historial). Distinta de is_active (inactivar).';
COMMENT ON COLUMN admin.mandate_signers.deleted_by IS 'ADR-0061 · Usuario que eliminó (sin FK, patrón de auditoría).';
COMMENT ON COLUMN admin.mandate_signers.document_number IS '@pii:high — Número de documento (Ley 1581). Nulo solo si signer_model = formato_blanco (HU #13128 AC8).';
COMMENT ON COLUMN admin.company_ot_mandate_rules.configured_by_scope IS 'ADR-0061 · Origen de la configuración: organismo | compania | super_admin.';
COMMENT ON COLUMN admin.transit_office_mandate_config.configured_by_scope IS 'ADR-0061 · Origen de la configuración: organismo | compania | super_admin.';
COMMENT ON COLUMN admin.mandate_signer_companies.configured_by_scope IS 'ADR-0061 (D-7) · Origen del vínculo mandatario-compañía: organismo | compania | super_admin.';
