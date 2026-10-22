-- HU #13604 (Feature #13602, Épica #13517) — SIMULACIÓN de la recodificación COMP-XXXX. Solo lectura: no modifica nada.
--
-- Muestra qué código recibirá cada tenant cuando corra la migración 20261015180000_HU13604_CodigoTenantSecuencial
-- (DDL 136-HU13604-codigo-tenant-secuencial.sql). Usa exactamente el mismo orden (created_at, id) y el mismo formato.
--
-- Cómo usarlo, en cada ambiente (DEV, QA, PDN) ANTES de desplegar:
--   1. Correr este script con un rol que pueda leer identity.tenants y admin.transit_office_profiles.
--   2. Revisar el resumen (primera consulta) y el mapeo completo (segunda consulta).
--   3. Pegar el resumen en la Discussion de la HU #13604 y, si el mapeo es correcto, desplegar.
-- Si la migración ya corrió en ese ambiente, el resumen lo indica (el mapeo deja de ser útil: el código anterior está
-- en identity.tenants.legacy_code). El script no referencia legacy_code para poder correr ANTES de la migración.

SET row_security = off;

-- 1. Resumen
SELECT CASE WHEN EXISTS (SELECT 1 FROM information_schema.columns
                          WHERE table_schema = 'identity' AND table_name = 'tenants' AND column_name = 'legacy_code')
            THEN 'YA APLICADA: la recodificación ya corrió en este ambiente'
            ELSE 'PENDIENTE: este es el resultado que producirá la migración'
       END                                                                       AS estado,
       count(*)                                                                  AS tenants_a_recodificar,
       count(*) FILTER (WHERE p.tenant_id IS NOT NULL)                           AS de_ellos_organismos_de_transito,
       'COMP-0001'                                                               AS primer_codigo,
       'COMP-' || CASE WHEN count(*) < 10000 THEN lpad(count(*)::text, 4, '0') ELSE count(*)::text END AS ultimo_codigo,
       'COMP-' || CASE WHEN count(*) + 1 < 10000 THEN lpad((count(*) + 1)::text, 4, '0') ELSE (count(*) + 1)::text END
                                                                                 AS siguiente_compania_nueva
FROM identity.tenants t
LEFT JOIN admin.transit_office_profiles p ON p.tenant_id = t.id;

-- 2. Mapeo completo, en el orden en que se numera
SELECT row_number() OVER (ORDER BY t.created_at, t.id)                           AS n,
       t.id,
       t.legal_name                                                              AS razon_social,
       t.tax_id                                                                  AS nit,
       CASE WHEN p.tenant_id IS NOT NULL THEN 'OT' ELSE t.tenant_type END        AS tipo,
       t.created_at,
       t.code                                                                    AS codigo_actual,
       'COMP-' || CASE WHEN row_number() OVER (ORDER BY t.created_at, t.id) < 10000
                       THEN lpad(row_number() OVER (ORDER BY t.created_at, t.id)::text, 4, '0')
                       ELSE row_number() OVER (ORDER BY t.created_at, t.id)::text
                  END                                                            AS codigo_nuevo
FROM identity.tenants t
LEFT JOIN admin.transit_office_profiles p ON p.tenant_id = t.id
ORDER BY t.created_at, t.id;
