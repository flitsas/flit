-- Bug #12594 — idempotencia de la firma de impronta manual por trámite + hash.
-- Migración: B12594_VehicleSignatureImprintsIdempotenciaPorTramite
--
-- El índice único parcial de 99-vehicle-signature-imprints-partial-unique-hash.sql era GLOBAL
-- sobre document_hash (SHA-256 del PDF base). Como el hash es solo del contenido del archivo,
-- el mismo PDF de impronta cargado en dos trámites distintos bloqueaba la segunda auditoría
-- (23505) aunque fuera un trámite diferente. La idempotencia correcta es por
-- (procedure_instance_id, document_hash) entre filas activas.
--
-- Sin datos: solo cambia el índice. Idempotente y re-ejecutable (DROP IF EXISTS / CREATE IF NOT EXISTS).

DROP INDEX IF EXISTS tramites.uq_vehicle_signature_imprints_document_hash_active;

CREATE UNIQUE INDEX IF NOT EXISTS uq_vehicle_signature_imprints_instance_document_hash_active
  ON tramites.vehicle_signature_imprints (procedure_instance_id, document_hash)
  WHERE deleted_at IS NULL;

COMMENT ON INDEX tramites.uq_vehicle_signature_imprints_instance_document_hash_active IS
  'Un solo registro activo por (trámite, hash del PDF base); el mismo PDF puede firmarse en trámites distintos. Bug #12594.';
COMMENT ON COLUMN tramites.vehicle_signature_imprints.document_hash IS
  'SHA-256 hex del PDF de impronta antes de estampar (idempotencia por trámite entre filas activas).';
