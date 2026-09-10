-- HU #11269 (D10/D13) — índice por expresión para la vista agrupada por persona.
-- Soporta DISTINCT ON (tenant, documento normalizado) ORDER BY created_at DESC
-- (HU #11270) sin full scan sobre procedure_instance_biometric_validations.
--
-- Normalización alineada con DocumentCanonicalNormalization (Trim + Upper):
--   upper(btrim(document_type)), upper(btrim(document_number))
-- soft-delete: deleted_at IS NULL (columna añadida en HU #10865).
--
-- NO es único: una persona puede tener N validaciones históricas.
-- DDL IDEMPOTENTE.

CREATE INDEX IF NOT EXISTS ix_biometric_validations_doc_norm_created
    ON tramites.procedure_instance_biometric_validations (
        tenant_id,
        upper(btrim(document_type)),
        upper(btrim(document_number)),
        created_at DESC
    )
    WHERE deleted_at IS NULL;

COMMENT ON INDEX tramites.ix_biometric_validations_doc_norm_created IS
    'HU #11269 — agrupa por persona (tenant + doc Trim+Upper) ordenado por created_at DESC para DISTINCT ON.';
