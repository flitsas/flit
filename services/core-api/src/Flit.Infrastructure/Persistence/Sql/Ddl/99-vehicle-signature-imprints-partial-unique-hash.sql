-- HU #12116 — idempotencia de document_hash solo entre filas activas.
-- Tras reemplazar impronta: soft-delete conserva document_hash en historial;
-- el UNIQUE global bloqueaba una nueva firma del mismo PDF base (23505).
-- Idempotente para ambientes que ya aplicaron 97 con uq_vehicle_signature_imprints_document_hash.

ALTER TABLE tramites.vehicle_signature_imprints
  DROP CONSTRAINT IF EXISTS uq_vehicle_signature_imprints_document_hash;

DROP INDEX IF EXISTS tramites.ix_vehicle_signature_imprints_document_hash;

CREATE UNIQUE INDEX IF NOT EXISTS uq_vehicle_signature_imprints_document_hash_active
  ON tramites.vehicle_signature_imprints (document_hash)
  WHERE deleted_at IS NULL;

COMMENT ON INDEX tramites.uq_vehicle_signature_imprints_document_hash_active IS
  'Un solo registro activo por hash del PDF base; soft-deleted conserva historial de auditoría.';
COMMENT ON COLUMN tramites.vehicle_signature_imprints.document_hash IS
  'SHA-256 hex del PDF de impronta antes de estampar (idempotencia entre filas activas).';
