-- ─────────────────────────────────────────────────────────────────────────────
-- HU #11308 (Feature #11301) — traslado de las certificaciones ya guardadas al
-- modelo canónico (ADR-0041). Fase 1 del plan de migración.
--
-- QUÉ HACE Y QUÉ NO:
--   SOLO TRASLADA. Si una celda estaba vacía en field_values, sigue vacía aquí.
--   No inventa datos, no consulta a ningún proveedor y no repara nada (D7): el
--   fix aplica hacia adelante. Los trámites ya cursados con celdas huecas se
--   quedan como están, y esa es la deuda que deja el diseño anterior — no hay
--   forma de saldarla retroactivamente sin volver a pagar las consultas.
--
-- POR QUÉ SE HACE IGUAL: no cuesta nada, no llama a nadie, y deja a los trámites
-- vivos leyendo por el mismo camino que los nuevos en vez de por el respaldo.
--
-- NO DISPARA EL TRIGGER DE INMUTABILIDAD: escribe en tablas NUEVAS, no en
-- field_values. No hace falta DISABLE TRIGGER USER.
--
-- IDEMPOTENTE: ON CONFLICT DO NOTHING sobre la llave natural. Re-ejecutarlo no
-- duplica ni pisa lo que ya haya escrito una consulta real (que además tiene
-- procedencia más fuerte que 'legacy').
-- ─────────────────────────────────────────────────────────────────────────────

-- Fechas de field_values: texto libre en tres formas observadas. Lo que no encaje
-- deja el canónico en NULL y conserva el crudo — misma regla que el dominio.
CREATE OR REPLACE FUNCTION tramites.fn_backfill_parse_fecha(raw text)
RETURNS date
LANGUAGE plpgsql
IMMUTABLE
AS $$
BEGIN
    IF raw IS NULL OR btrim(raw) = '' THEN
        RETURN NULL;
    END IF;

    -- ISO, con o sin parte de hora: se toma el día tal cual viene. No se convierte
    -- a UTC a propósito: convertir es justo lo que puede correr el día impreso en
    -- un certificado cuando la hora es alta.
    IF raw ~ '^\d{4}-\d{2}-\d{2}' THEN
        RETURN substring(raw from 1 for 10)::date;
    END IF;

    IF raw ~ '^\d{2}/\d{2}/\d{4}$' THEN
        RETURN to_date(raw, 'DD/MM/YYYY');
    END IF;

    IF raw ~ '^\d{2}-\d{2}-\d{4}$' THEN
        RETURN to_date(raw, 'DD-MM-YYYY');
    END IF;

    RETURN NULL;
EXCEPTION WHEN OTHERS THEN
    -- Una fecha imposible (31/02) no puede tumbar el traslado completo.
    RETURN NULL;
END;
$$;

-- Vocabulario de vigencia. 'APROBADA' NO se mapea a vigente: describe el estado
-- del trámite de la revisión, no su cobertura.
CREATE OR REPLACE FUNCTION tramites.fn_backfill_vigencia(raw text)
RETURNS varchar(12)
LANGUAGE sql
IMMUTABLE
AS $$
    SELECT CASE upper(btrim(coalesce(raw, '')))
        WHEN 'VIGENTE'     THEN 'vigente'
        WHEN 'SI'          THEN 'vigente'
        WHEN 'VENCIDO'     THEN 'vencido'
        WHEN 'NO VIGENTE'  THEN 'vencido'
        WHEN 'NO_VIGENTE'  THEN 'vencido'
        WHEN 'NO'          THEN 'vencido'
        WHEN 'NO APLICA'   THEN 'no_aplica'
        ELSE 'unknown'
    END::varchar(12);
$$;

-- ============================================================================
-- 1) SOAT — una fila por instancia que tenga al menos una celda con dato
-- ============================================================================
WITH datos AS (
    SELECT
        fv.procedure_instance_id,
        fv.tenant_id,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'soat_poliza')       AS poliza,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'soat_aseguradora')  AS aseguradora,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'soat_expedicion')   AS expedicion,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'soat_vigencia')     AS vigencia,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'soat_vencimiento')  AS vencimiento,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'soat_estado')       AS estado,
        -- Procedencia: el momento de la consulta que ya se venía guardando. Sin él,
        -- el created_at de la fila más antigua es la mejor aproximación honesta.
        max(fv.value_text) FILTER (WHERE fv.field_key = 'runt_consulta_fecha') AS consulta_fecha,
        min(fv.created_at)                                                    AS observado_en
    FROM tramites.procedure_instance_field_values fv
    WHERE fv.field_key IN (
        'soat_poliza', 'soat_aseguradora', 'soat_expedicion', 'soat_vigencia',
        'soat_vencimiento', 'soat_estado', 'runt_consulta_fecha')
    GROUP BY fv.procedure_instance_id, fv.tenant_id
)
INSERT INTO tramites.vehicle_soat_policies (
    tenant_id, procedure_instance_id, natural_key,
    policy_number, policy_number_raw,
    insurer_name, insurer_name_raw,
    issued_on, issued_on_raw,
    valid_from, valid_from_raw,
    valid_until, valid_until_raw,
    vigency_status, vigency_status_raw,
    is_current, source_kind, provider_key, observed_at, mapper_version, created_at)
SELECT
    d.tenant_id,
    d.procedure_instance_id,
    -- Misma composición que SoatCertification.NaturalKey(): numero|vencimiento,
    -- con '~' para el tramo ausente.
    coalesce(nullif(btrim(d.poliza), ''), '~') || '|'
        || coalesce(to_char(tramites.fn_backfill_parse_fecha(d.vencimiento), 'YYYY-MM-DD'), '~'),
    nullif(btrim(d.poliza), ''), d.poliza,
    nullif(btrim(d.aseguradora), ''), d.aseguradora,
    tramites.fn_backfill_parse_fecha(d.expedicion), d.expedicion,
    tramites.fn_backfill_parse_fecha(d.vigencia), d.vigencia,
    tramites.fn_backfill_parse_fecha(d.vencimiento), d.vencimiento,
    tramites.fn_backfill_vigencia(d.estado), d.estado,
    true,
    -- 'system' y no 'consultation': esta fila la produjo un traslado, no una
    -- consulta. Declararla como consulta le daría una precedencia que no tiene y
    -- bloquearía que una consulta real la mejore.
    'system', 'legacy',
    coalesce(
        tramites.fn_backfill_parse_fecha(d.consulta_fecha)::timestamptz,
        d.observado_en),
    'legacy',
    now()
FROM datos d
WHERE coalesce(
        nullif(btrim(d.poliza), ''), nullif(btrim(d.aseguradora), ''),
        nullif(btrim(d.expedicion), ''), nullif(btrim(d.vigencia), ''),
        nullif(btrim(d.vencimiento), ''), nullif(btrim(d.estado), '')) IS NOT NULL
ON CONFLICT (procedure_instance_id, natural_key) DO NOTHING;

-- ============================================================================
-- 2) RTM — misma lógica
-- ============================================================================
WITH datos AS (
    SELECT
        fv.procedure_instance_id,
        fv.tenant_id,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'rtm_numero')       AS numero,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'rtm_entidad')      AS entidad,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'rtm_expedicion')   AS expedicion,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'rtm_vigencia')     AS vigencia,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'rtm_vencimiento')  AS vencimiento,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'rtm_estado')       AS estado,
        max(fv.value_text) FILTER (WHERE fv.field_key = 'runt_consulta_fecha') AS consulta_fecha,
        min(fv.created_at)                                                  AS observado_en
    FROM tramites.procedure_instance_field_values fv
    WHERE fv.field_key IN (
        'rtm_numero', 'rtm_entidad', 'rtm_expedicion', 'rtm_vigencia',
        'rtm_vencimiento', 'rtm_estado', 'runt_consulta_fecha')
    GROUP BY fv.procedure_instance_id, fv.tenant_id
)
INSERT INTO tramites.vehicle_rtm_inspections (
    tenant_id, procedure_instance_id, natural_key,
    certificate_number, certificate_number_raw,
    cda_name, cda_name_raw,
    issued_on, issued_on_raw,
    valid_from, valid_from_raw,
    valid_until, valid_until_raw,
    vigency_status, vigency_status_raw,
    is_current, source_kind, provider_key, observed_at, mapper_version, created_at)
SELECT
    d.tenant_id,
    d.procedure_instance_id,
    coalesce(nullif(btrim(d.numero), ''), '~') || '|'
        || coalesce(to_char(tramites.fn_backfill_parse_fecha(d.vencimiento), 'YYYY-MM-DD'), '~'),
    nullif(btrim(d.numero), ''), d.numero,
    nullif(btrim(d.entidad), ''), d.entidad,
    tramites.fn_backfill_parse_fecha(d.expedicion), d.expedicion,
    tramites.fn_backfill_parse_fecha(d.vigencia), d.vigencia,
    tramites.fn_backfill_parse_fecha(d.vencimiento), d.vencimiento,
    tramites.fn_backfill_vigencia(d.estado), d.estado,
    true, 'system', 'legacy',
    coalesce(
        tramites.fn_backfill_parse_fecha(d.consulta_fecha)::timestamptz,
        d.observado_en),
    'legacy',
    now()
FROM datos d
WHERE coalesce(
        nullif(btrim(d.numero), ''), nullif(btrim(d.entidad), ''),
        nullif(btrim(d.expedicion), ''), nullif(btrim(d.vigencia), ''),
        nullif(btrim(d.vencimiento), ''), nullif(btrim(d.estado), '')) IS NOT NULL
ON CONFLICT (procedure_instance_id, natural_key) DO NOTHING;

-- ============================================================================
-- 3) Registro mercantil — una fila por entrada del snapshot congelado
-- ============================================================================
-- rues_snapshots_json tiene la forma { "<nit>": { "queriedAt": "…", "fields": {…} } }.
-- Es la fuente preferida sobre las llaves `rues_*` sueltas, que son de INSTANCIA y
-- solo pueden representar a UNA compañía: en un traspaso entre dos personas
-- jurídicas la segunda consulta pisaba a la primera.
WITH snapshots AS (
    SELECT
        fv.procedure_instance_id,
        fv.tenant_id,
        fv.created_at,
        entrada.key                        AS nit,
        entrada.value -> 'fields'          AS campos,
        entrada.value ->> 'queriedAt'      AS consultado_en
    FROM tramites.procedure_instance_field_values fv
    CROSS JOIN LATERAL jsonb_each(
        CASE
            WHEN fv.value_text IS NOT NULL AND btrim(fv.value_text) LIKE '{%'
            THEN fv.value_text::jsonb
            ELSE '{}'::jsonb
        END) AS entrada
    WHERE fv.field_key = 'rues_snapshots_json'
)
INSERT INTO tramites.company_registrations (
    tenant_id, procedure_instance_id, nit,
    business_name, business_name_raw,
    registration_number, registration_number_raw,
    registration_status, registration_status_raw,
    registered_on, registered_on_raw,
    renewed_on, renewed_on_raw,
    chamber_of_commerce, chamber_of_commerce_raw,
    category, category_raw,
    address, address_raw,
    city, city_raw,
    source_kind, provider_key, observed_at, mapper_version, created_at)
SELECT
    s.tenant_id,
    s.procedure_instance_id,
    btrim(s.nit),
    nullif(btrim(s.campos ->> 'rues_razon_social'), ''),        s.campos ->> 'rues_razon_social',
    nullif(btrim(s.campos ->> 'rues_matricula_mercantil'), ''), s.campos ->> 'rues_matricula_mercantil',
    -- El RUES usa ACTIVA/CANCELADA, vocabulario distinto al del vehículo.
    CASE upper(btrim(coalesce(s.campos ->> 'rues_estado', '')))
        WHEN 'ACTIVA'     THEN 'vigente'
        WHEN 'ACTIVO'     THEN 'vigente'
        WHEN 'CANCELADA'  THEN 'vencido'
        WHEN 'CANCELADO'  THEN 'vencido'
        WHEN 'LIQUIDADA'  THEN 'vencido'
        ELSE 'unknown'
    END::varchar(12),                                            s.campos ->> 'rues_estado',
    tramites.fn_backfill_parse_fecha(s.campos ->> 'rues_fecha_matricula'),  s.campos ->> 'rues_fecha_matricula',
    tramites.fn_backfill_parse_fecha(s.campos ->> 'rues_fecha_renovacion'), s.campos ->> 'rues_fecha_renovacion',
    nullif(btrim(s.campos ->> 'rues_camara_comercio'), ''),     s.campos ->> 'rues_camara_comercio',
    nullif(btrim(s.campos ->> 'rues_categoria'), ''),           s.campos ->> 'rues_categoria',
    nullif(btrim(s.campos ->> 'rues_direccion'), ''),           s.campos ->> 'rues_direccion',
    nullif(btrim(s.campos ->> 'rues_municipio'), ''),           s.campos ->> 'rues_municipio',
    'system', 'legacy',
    coalesce(nullif(s.consultado_en, '')::timestamptz, s.created_at),
    'legacy',
    now()
FROM snapshots s
WHERE btrim(coalesce(s.nit, '')) <> ''
  AND s.campos IS NOT NULL
  AND nullif(btrim(coalesce(s.campos ->> 'rues_razon_social', '')), '') IS NOT NULL
ON CONFLICT (procedure_instance_id, nit) DO NOTHING;

-- Las funciones auxiliares solo sirven a este traslado.
DROP FUNCTION IF EXISTS tramites.fn_backfill_parse_fecha(text);
DROP FUNCTION IF EXISTS tramites.fn_backfill_vigencia(text);
