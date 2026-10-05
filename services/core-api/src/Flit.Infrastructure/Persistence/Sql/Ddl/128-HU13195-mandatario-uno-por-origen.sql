-- HU #13195 (Feature #13116 F4, Épica #13090 Mandatarios) — ADR-0066 D1 / duda D-1. Migración: 20260930180000_HU13195_MandatarioUnoPorOrigen.
--
-- Qué hace (sin tablas ni columnas nuevas, sin RLS nuevo; admin.mandate_signer_companies no lleva RLS):
--   1. COLAPSO sin borrar. Por cada (organismo, compañía, grupo de origen) con más de un vínculo activo deja activo UNO:
--        (a) el mandatario designado en admin.company_ot_mandate_rules.default_mandate_signer_id de esa compañía×OT;
--        (b) si no, el de FIRMA VÁLIDA (mandatario activo, no eliminado, vigencia propia al día y firma vigente:
--            baúl activo y vigente del documento en el tenant de la compañía, o biometría/identidad aprobada y vigente;
--            los modelos juridica y formato_blanco no tienen firma personal que validar);
--        (c) si no, el vínculo más reciente (created_at, luego id).
--      Los demás pasan a is_active = false. No se borra ningún mandatario, vínculo ni historial.
--      Grupo de origen: 'organismo' y 'super_admin' son UN grupo; 'compania' es otro (ADR-0066 D1).
--   2. ÍNDICE ÚNICO PARCIAL por expresión uq_mandate_signer_companies_one_per_origin (no es representable en EF Core:
--      vive solo en este DDL, como ix_mandate_signers_alive). El índice de ADR-0036 uq_mandate_signer_companies_active se conserva.
-- Idempotente: re-ejecutar no encuentra grupos N>1 (el índice lo impide) y el CREATE usa IF NOT EXISTS.
-- Reversible: la reversa solo elimina el índice; NO reactiva los vínculos colapsados (ver Down en la migración).
-- Reporte previo de solo lectura (sin datos personales): GET /api/v1/admin/mandate-signers/link-collapse-report (Super Admin)
-- y el script services/core-api/docs/sql/hu-13195-reporte-colapso-vinculos-mandatario.sql (se corre ANTES de desplegar).

DO $mig$
DECLARE
    v_collapsed integer;
BEGIN
    WITH links AS (
        SELECT c.id AS link_id,
               c.transit_office_id,
               c.company_tenant_id,
               c.created_at,
               CASE WHEN c.configured_by_scope = 'compania' THEN 'compania' ELSE 'organismo' END AS origin_group,
               EXISTS (SELECT 1 FROM admin.company_ot_mandate_rules r
                        WHERE r.company_tenant_id = c.company_tenant_id
                          AND r.transit_office_id = c.transit_office_id
                          AND r.default_mandate_signer_id = c.mandate_signer_id) AS is_designated,
               (s.is_active
                AND s.deleted_at IS NULL
                AND (s.validity_kind = 'fixed'
                     OR ((now() AT TIME ZONE 'America/Bogota')::date BETWEEN s.valid_from AND s.valid_to))
                AND (s.signer_model <> 'natural'
                     OR (CASE
                             WHEN COALESCE(s.signature_method,
                                           CASE WHEN s.signature_vault_id IS NOT NULL THEN 'baul' ELSE 'biometria' END) = 'baul'
                             THEN EXISTS (SELECT 1 FROM admin.signature_vault v
                                           WHERE v.tenant_id = c.company_tenant_id
                                             AND upper(btrim(v.document_type))   = upper(btrim(s.document_type))
                                             AND upper(btrim(v.document_number)) = upper(btrim(s.document_number))
                                             AND v.estado = 'activa'
                                             AND (now() AT TIME ZONE 'America/Bogota')::date
                                                 BETWEEN v.vigencia_desde AND v.vigencia_hasta)
                             ELSE EXISTS (SELECT 1 FROM tramites.procedure_instance_biometric_validations b
                                           WHERE b.tenant_id = c.company_tenant_id
                                             AND b.status = 'aprobado'
                                             AND b.deleted_at IS NULL
                                             AND b.valid_until > now()
                                             AND upper(btrim(b.document_type))   = upper(btrim(s.document_type))
                                             AND upper(btrim(b.document_number)) = upper(btrim(s.document_number)))
                                  OR EXISTS (SELECT 1 FROM admin.admin_identity_validations iv
                                              WHERE iv.id = s.identity_validation_ref
                                                AND iv.status = 'aprobado'
                                                AND iv.valid_until > now())
                         END))) AS has_valid_signature
          FROM admin.mandate_signer_companies c
          JOIN admin.mandate_signers s ON s.id = c.mandate_signer_id
         WHERE c.is_active
    ),
    ranked AS (
        SELECT l.*,
               count(*) OVER (PARTITION BY l.transit_office_id, l.company_tenant_id, l.origin_group) AS group_size,
               row_number() OVER (PARTITION BY l.transit_office_id, l.company_tenant_id, l.origin_group
                                  ORDER BY l.is_designated DESC, l.has_valid_signature DESC, l.created_at DESC, l.link_id DESC) AS rn
          FROM links l
    ),
    done AS (
        UPDATE admin.mandate_signer_companies c
           SET is_active = false
          FROM ranked r
         WHERE r.link_id = c.id AND r.group_size > 1 AND r.rn > 1
        RETURNING c.id
    )
    SELECT count(*) INTO v_collapsed FROM done;

    RAISE NOTICE 'HU13195: % vínculos mandatario-compañía inactivados por el colapso (sin borrar).', v_collapsed;
END
$mig$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_mandate_signer_companies_one_per_origin
    ON admin.mandate_signer_companies
       (transit_office_id, company_tenant_id,
        (CASE WHEN configured_by_scope = 'compania' THEN 'compania' ELSE 'organismo' END))
    WHERE is_active;

COMMENT ON INDEX admin.uq_mandate_signer_companies_one_per_origin IS 'ADR-0066 D1 · Un solo vínculo activo por (organismo, compañía, grupo de origen): organismo+super_admin es un grupo; compania es otro. Por expresión, fuera del modelo EF.';
