-- La impronta vuelve al checklist de carga del gestor.
--
-- La 46 (HU #11181) la marco como documento generado por el sistema, y la 94 hizo que esa marca
-- excluyera del checklist. Efecto: aunque la impronta siga siendo requisito obligatorio del tipo
-- en `procedure_document_requirements`, el gestor no la veia en Requisitos —ni para adjuntarla ni
-- para generarla— y el paso se declaraba «Documentos completos» sin ella.
--
-- La impronta no es como el FUR o el mandato: FLIT puede generarla (Kyverum) pero el organismo
-- tambien la entrega en papel, y por eso el tipo de tramite ofrece «el gestor elige» en
-- `gate_profile.improntaSource`. Esa preferencia quedaba huerfana: el boton «Generar impronta»
-- vive dentro de la tarjeta del checklist, que no se pintaba.
--
-- `generated_sort_order` se conserva: la impronta se sigue ordenando en el consolidado y en la
-- prelacion del OT; lo unico que cambia es que vuelve a pedirse al gestor.

UPDATE tramites.document_types
   SET is_system_generated = false,
       updated_at = now()
 WHERE code = 'impronta'
   AND is_system_generated IS DISTINCT FROM false;
