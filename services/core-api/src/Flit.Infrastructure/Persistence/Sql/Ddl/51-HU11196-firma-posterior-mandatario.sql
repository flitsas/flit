-- HU #11196 (ajuste tras validación manual) — la firma a posteriori también cubre al MANDATARIO.
--
-- Hasta ahora la marca solo existía para las partes del trámite (comprador/vendedor), que siempre son
-- una empresa representada: de ahí que `company_document_number` fuera NOT NULL.
--
-- El mandatario no representa a ninguna de las partes: es quien firma el mandato en nombre de la
-- compañía gestora ante el organismo. Para él no hay NIT representado que anotar, y rellenarlo con un
-- valor inventado (cadena vacía o el NIT de la gestora) haría que la traza del lote afirmara un vínculo
-- que no existe.
--
-- Solo se afloja la restricción; no se toca ninguna fila existente ni el índice único parcial, que
-- sigue admitiendo una marca pendiente por (trámite, parte) y ahora acepta 'mandatario' como parte.
-- DDL IDEMPOTENTE.

ALTER TABLE tramites.deferred_signature_marks
    ALTER COLUMN company_document_number DROP NOT NULL;

COMMENT ON COLUMN tramites.deferred_signature_marks.company_document_number IS
    'NIT de la empresa representada, solo para la traza del lote. NULL cuando la parte es el mandatario: '
    'no representa a ninguna de las partes del trámite.';
