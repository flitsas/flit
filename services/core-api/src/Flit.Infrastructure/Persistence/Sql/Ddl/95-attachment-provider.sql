-- Procedencia de proveedor externo en adjuntos (impronta Kyverum vs carga manual).
-- DDL IDEMPOTENTE. null = manual / sin proveedor.

ALTER TABLE tramites.procedure_instance_attachments
  ADD COLUMN IF NOT EXISTS provider varchar(40);

COMMENT ON COLUMN tramites.procedure_instance_attachments.provider
  IS 'Proveedor externo del adjunto (p. ej. kyverum). null = carga manual u origen no proveedor.';
