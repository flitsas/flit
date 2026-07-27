-- HU #10936 (Feature #10929, RL-flujo-ajustes) — Escritura del trámite: persistir la escritura usada.
-- Se agrega la referencia (nullable) a la escritura (admin.company_deeds.id) que entró al registro para
-- las escrituras de sistema (adjuntos tipo 'escritura'/'escritura_comprador'). Deja trazable cuál
-- escritura se usó y sostiene el "congelado" tras la entrega del trámite. DDL IDEMPOTENTE.

ALTER TABLE tramites.procedure_instance_attachments
  ADD COLUMN IF NOT EXISTS source_deed_id uuid;

COMMENT ON COLUMN tramites.procedure_instance_attachments.source_deed_id
  IS 'HU #10936 — escritura (admin.company_deeds.id) utilizada; solo en adjuntos escritura/escritura_comprador de sistema.';
