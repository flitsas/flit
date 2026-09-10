-- Plantilla propia de mandato por OT (PDF blank u editor).
-- Si kind = none → redacciones default del sistema (template_code).
-- DDL IDEMPOTENTE.

ALTER TABLE admin.transit_office_mandate_config
  ADD COLUMN IF NOT EXISTS custom_template_kind varchar(20) NOT NULL DEFAULT 'none',
  ADD COLUMN IF NOT EXISTS custom_template_storage_path varchar(1000),
  ADD COLUMN IF NOT EXISTS custom_template_sha256 varchar(64),
  ADD COLUMN IF NOT EXISTS custom_template_file_name varchar(260),
  ADD COLUMN IF NOT EXISTS custom_template_body text,
  ADD COLUMN IF NOT EXISTS custom_field_manifest jsonb;

ALTER TABLE admin.transit_office_mandate_config
  DROP CONSTRAINT IF EXISTS ck_transit_office_mandate_config_custom_kind;
ALTER TABLE admin.transit_office_mandate_config
  ADD CONSTRAINT ck_transit_office_mandate_config_custom_kind
  CHECK (custom_template_kind IN ('none', 'pdf', 'editor'));
