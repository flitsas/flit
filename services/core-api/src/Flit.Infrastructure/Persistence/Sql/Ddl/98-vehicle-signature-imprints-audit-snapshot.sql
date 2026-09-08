-- HU #12116 — opción 1+3: conservar auditoría al reemplazar impronta firmada.
-- Idempotente para ambientes que ya aplicaron 97 con attachment_id NOT NULL + RESTRICT.

ALTER TABLE tramites.vehicle_signature_imprints
  DROP CONSTRAINT IF EXISTS fk_vehicle_signature_imprints_attachment;

ALTER TABLE tramites.vehicle_signature_imprints
  ALTER COLUMN attachment_id DROP NOT NULL;

ALTER TABLE tramites.vehicle_signature_imprints
  ADD CONSTRAINT fk_vehicle_signature_imprints_attachment
  FOREIGN KEY (attachment_id)
  REFERENCES tramites.procedure_instance_attachments(id)
  ON DELETE SET NULL ON UPDATE CASCADE;

ALTER TABLE tramites.vehicle_signature_imprints
  ADD COLUMN IF NOT EXISTS signed_storage_path text;

ALTER TABLE tramites.vehicle_signature_imprints
  ADD COLUMN IF NOT EXISTS signed_sha256 varchar(64);

ALTER TABLE tramites.vehicle_signature_imprints
  ADD COLUMN IF NOT EXISTS signed_size_bytes bigint;

ALTER TABLE tramites.vehicle_signature_imprints
  ADD COLUMN IF NOT EXISTS signed_filename varchar(255);

DROP INDEX IF EXISTS tramites.ix_vehicle_signature_imprints_attachment;
CREATE INDEX IF NOT EXISTS ix_vehicle_signature_imprints_attachment
  ON tramites.vehicle_signature_imprints (attachment_id)
  WHERE attachment_id IS NOT NULL;

COMMENT ON COLUMN tramites.vehicle_signature_imprints.attachment_id IS
  'Adjunto vigente al firmar; NULL tras reemplazo/borrado (historial conservado con soft-delete + snapshot).';
COMMENT ON COLUMN tramites.vehicle_signature_imprints.signed_storage_path IS
  'Path del PDF firmado en storage (snapshot; no borrar al reemplazar el adjunto).';
COMMENT ON COLUMN tramites.vehicle_signature_imprints.signed_sha256 IS
  'SHA-256 del PDF firmado almacenado (distinto de document_hash del PDF base).';
