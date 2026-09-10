-- =============================================================================
-- Entidad ante la que se levantó la prenda, para el párrafo 23 del FUR.
-- Migración: 20260825170000_PrendaEntidadLevantamiento
--
-- El numeral 20 del FUR ya declara QUIÉN era el acreedor (columna OTRO marcada + «A FAVOR DE»), pero
-- el recuadro de observaciones de un levantamiento tiene que decir DÓNDE se hizo: la notaría, oficina
-- de registro o entidad ante la que se extinguió el gravamen. Ese dato no lo trae el RUNT —su detalle
-- de gravamen se limita a acreedor, documento, fecha y estado— así que hay que capturarlo.
--
-- Nullable a propósito: solo lo exige el trámite de LEVANTAMIENTO_PRENDA (donde el levantamiento ES
-- el trámite). En traspaso y en matrícula la decisión «levantar» es una entre varias y conserva su
-- texto actual; ahí la columna queda vacía y el FUR no cambia.
--
-- Idempotente y reaplicable.
-- =============================================================================

ALTER TABLE tramites.procedure_instance_prenda
  ADD COLUMN IF NOT EXISTS levantamiento_entidad varchar(200);

COMMENT ON COLUMN tramites.procedure_instance_prenda.levantamiento_entidad
  IS 'Entidad/oficina ante la que se levantó el gravamen. Alimenta el párrafo 23 del FUR en el trámite de levantamiento de prenda; vacía en traspaso y matrícula, que conservan su literal.';
