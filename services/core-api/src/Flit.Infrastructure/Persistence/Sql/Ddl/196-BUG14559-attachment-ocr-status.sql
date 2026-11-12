-- Bug #14559 (PDN) — veredicto OCR del adjunto: informativo, declarado por el cliente al registrar; NO es compuerta. [ADR-0085]
-- Migración: BUG14559_AttachmentOcrStatus.
--
-- Por qué: el veredicto OCR del wizard (verified/rejected/skipped) se calculaba solo en el cliente y se perdía al
-- cambiar de paso; un documento rechazado volvía a verse «Cargado». El adjunto registra ahora el veredicto con que se
-- subió. Como el reemplazo de un tipo crea fila nueva, el veredicto se resetea solo con el archivo.
--
-- Qué hace (aditiva, nullable, SIN backfill: los adjuntos previos quedan en NULL = «Cargado»):
--   1. tramites.procedure_instance_attachments.ocr_status varchar(20) NULL — verified | rejected | skipped.
--   2. tramites.procedure_instance_attachments.ocr_reason varchar(30) NULL — código cerrado del motivo, solo con
--      rejected/skipped: vin_mismatch | tipo_invalido | no_confirmado | ilegible | excede_tamano | analisis_fallido.
--      Nunca texto libre del documento (Ley 1581, minimización): ni observaciones del modelo ni VINs.
--   3. Sin RLS nueva (la tabla ya tiene tenant_isolation) ni índices (solo se lee junto a la fila del adjunto).
--      El trigger tr_procedure_instance_attachments_audit (public.trg_audit_log, to_jsonb(NEW)/to_jsonb(OLD)) no tiene
--      lista de columnas: audita las dos nuevas sin cambios.
-- DDL idempotente (ADD COLUMN IF NOT EXISTS + CHECK con guarda en pg_constraint) y reversible (Down en la migración).

ALTER TABLE tramites.procedure_instance_attachments
    ADD COLUMN IF NOT EXISTS ocr_status varchar(20),
    ADD COLUMN IF NOT EXISTS ocr_reason varchar(30);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_procedure_instance_attachments_ocr_status'
    ) THEN
        ALTER TABLE tramites.procedure_instance_attachments
          ADD CONSTRAINT ck_procedure_instance_attachments_ocr_status
          CHECK (ocr_status IS NULL OR ocr_status IN ('verified','rejected','skipped'));
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_procedure_instance_attachments_ocr_reason'
    ) THEN
        ALTER TABLE tramites.procedure_instance_attachments
          ADD CONSTRAINT ck_procedure_instance_attachments_ocr_reason
          CHECK (ocr_reason IS NULL OR (ocr_status IS NOT NULL AND ocr_status IN ('rejected','skipped') AND ocr_reason IN
            ('vin_mismatch','tipo_invalido','no_confirmado','ilegible','excede_tamano','analisis_fallido')));
    END IF;
END $$;

COMMENT ON COLUMN tramites.procedure_instance_attachments.ocr_status IS
    'BUG14559 · Veredicto OCR declarado por el cliente al registrar: verified | rejected | skipped; NULL = sin dato (previo al fix, sin OCR o generado). Informativo, no es compuerta [ADR-0085].';
COMMENT ON COLUMN tramites.procedure_instance_attachments.ocr_reason IS
    'BUG14559 · Código cerrado del motivo (solo con rejected/skipped). Nunca texto libre del documento (Ley 1581).';
