-- HU #13195 (ADR-0066 D1) — REPORTE PREVIO de solo lectura del colapso de vínculos mandatario-compañía.
-- Córrelo ANTES de desplegar la migración 20260930180000_HU13195_MandatarioUnoPorOrigen (DEV y QA primero) y avisa
-- a los organismos y compañías afectados antes de PDN. No modifica datos y no trae datos personales (ids, compañía,
-- organismo, qué se conserva y por qué). La misma lógica la expone el endpoint de Super Admin
-- GET /api/v1/admin/mandate-signers/link-collapse-report. Mantener sincronizado con el DDL 128.
WITH links AS (
    SELECT c.id AS link_id, c.mandate_signer_id, c.transit_office_id, c.company_tenant_id, c.created_at,
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
)
SELECT r.transit_office_id, r.company_tenant_id, r.origin_group, r.group_size,
       r.link_id, r.mandate_signer_id,
       CASE WHEN r.rn = 1 THEN 'conservar' ELSE 'inactivar' END AS accion,
       CASE
           WHEN k.is_designated THEN 'designado_en_regla'
           WHEN k.has_valid_signature AND EXISTS (SELECT 1 FROM ranked o
                     WHERE o.transit_office_id = r.transit_office_id AND o.company_tenant_id = r.company_tenant_id
                       AND o.origin_group = r.origin_group AND NOT o.has_valid_signature) THEN 'firma_valida'
           ELSE 'mas_reciente'
       END AS criterio
  FROM ranked r
  JOIN ranked k ON k.transit_office_id = r.transit_office_id AND k.company_tenant_id = r.company_tenant_id
               AND k.origin_group = r.origin_group AND k.rn = 1
 WHERE r.group_size > 1
 ORDER BY r.transit_office_id, r.company_tenant_id, r.origin_group, r.rn;
