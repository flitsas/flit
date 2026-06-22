-- Trámites Rework — valores de campo "loose" (derivados de consulta/sistema, no atados a un form_field).
-- Bug detectado en verificación LIVE contra Postgres real: dos flujos nuevos escriben claves de
-- field_values que NO son form_fields (consulta vehículo M1 hidrata vehicle_brand/line/color/...,
-- y organismo M5 PATCHea transit_office_code/name/city). La FK NOT NULL a tramites.form_fields(id)
-- provocaba violación 23503 (...form_field_id_fkey) o rechazo "unknown_field".
--
-- Fix: form_field_id pasa a ser NULLABLE para admitir valores "loose" (FormFieldId = null).
-- Los valores ligados a un form_field real siguen funcionando igual.
ALTER TABLE tramites.procedure_instance_field_values
    ALTER COLUMN form_field_id DROP NOT NULL;
