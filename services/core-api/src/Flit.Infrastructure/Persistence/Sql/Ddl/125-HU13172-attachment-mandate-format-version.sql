-- HU #13172 (Feature #13118 F6, Épica #13090 Mandatarios) — versión del formato de mandato usada en el contrato emitido.
-- Migración: 20261001140341_HU13172_AttachmentMandateFormatVersion.
--
-- Por qué: el Super Admin puede publicar versiones nuevas de la plantilla de un formato (admin.mandate_format_versions,
-- DDL 124). Un contrato ya generado no puede cambiar de texto retroactivamente, y la regeneración del expediente
-- (FurCommand reemplaza el adjunto 'mandato' del sistema) debe reproducir la versión con la que se emitió. Para eso el
-- adjunto registra qué formato y qué versión usó.
--
-- Qué hace (aditiva, nullable, sin RLS nueva: la tabla ya la tiene; sin índices: solo se lee por el adjunto del trámite):
--   1. tramites.procedure_instance_attachments.mandate_format_code    varchar(30) NULL  — código del formato (template_code).
--   2. tramites.procedure_instance_attachments.mandate_format_version integer     NULL  — versión de plantilla usada:
--        NULL = adjunto que no es un mandato de sistema o mandato con la plantilla propia heredada del OT (HU #11705);
--        0    = redacción del generador, sin personalización;
--        N>=1 = versión N de admin.mandate_format_versions.
--   3. Backfill: los mandatos de sistema YA emitidos quedan con versión 0 (se generaron con la redacción del generador):
--      una publicación posterior no los altera al regenerarse. El código queda NULL (no se registró entonces).
-- DDL idempotente (ADD COLUMN IF NOT EXISTS; el backfill solo toca filas con la versión en NULL) y reversible (Down en
-- la migración).

ALTER TABLE tramites.procedure_instance_attachments
    ADD COLUMN IF NOT EXISTS mandate_format_code varchar(30),
    ADD COLUMN IF NOT EXISTS mandate_format_version integer;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'ck_procedure_instance_attachments_mandate_format_version'
    ) THEN
        ALTER TABLE tramites.procedure_instance_attachments
          ADD CONSTRAINT ck_procedure_instance_attachments_mandate_format_version
          CHECK (mandate_format_version IS NULL OR mandate_format_version >= 0);
    END IF;
END $$;

COMMENT ON COLUMN tramites.procedure_instance_attachments.mandate_format_code IS
    'HU13172 · Código del formato de mandato (template_code) con el que se generó el adjunto de tipo mandato; NULL en cualquier otro adjunto.';
COMMENT ON COLUMN tramites.procedure_instance_attachments.mandate_format_version IS
    'HU13172 · Versión de plantilla del formato usada al emitir el mandato: NULL = no aplica o plantilla propia del OT; 0 = redacción del generador; N = admin.mandate_format_versions.version_number.';

UPDATE tramites.procedure_instance_attachments
   SET mandate_format_version = 0
 WHERE tipo = 'mandato'
   AND source = 'system'
   AND mandate_format_version IS NULL;
