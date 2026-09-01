-- =============================================================================
-- TRASPASO_UNILATERAL: el paso de la parte entrante se llama «Locatario», no «Comprador».
--
-- En este trámite el vehículo lo formaliza a su nombre el LOCATARIO del leasing. El modelo de datos
-- lo persiste con el rol `comprador` —igual que la familia OTROS persiste a su titular— porque no
-- hay un rol `locatario` en un trámite de una sola parte entrante, y cambiarlo movería el gate, la
-- biometría y el FUR. Lo que estaba mal no es el rol: es el rótulo.
--
-- «Comprador» describe un contrato que aquí no existe: en un leasing nadie compra, y el propietario
-- (la arrendadora) no vende — autoriza. El gestor leía «Comprador» y buscaba a la contraparte de una
-- compraventa que este trámite no tiene.
--
-- Solo cambia el `title`, que es lo que se ve. NO se tocan `code` ni el `code` de la sección
-- (`COMPRADOR`): de ellos cuelgan `resolveActorRole`, `SectionCoversBuyer` y el guardado del paso.
--
-- Idempotente y reaplicable.
-- =============================================================================

UPDATE tramites.procedure_steps st
   SET title = 'Locatario',
       updated_at = now()
  FROM tramites.procedure_types pt
 WHERE st.procedure_type_id = pt.id
   AND pt.code = 'TRASPASO_UNILATERAL'
   AND st.code = 'comprador'
   AND st.title IS DISTINCT FROM 'Locatario';

-- Guarda: si el tipo tiene el paso, tiene que haber quedado rotulado. Un rótulo equivocado en el
-- paso de actores manda al gestor a capturar a la persona que no es.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
          FROM tramites.procedure_steps st
          JOIN tramites.procedure_types pt ON pt.id = st.procedure_type_id
         WHERE pt.code = 'TRASPASO_UNILATERAL'
           AND st.code = 'comprador')
       AND NOT EXISTS (
        SELECT 1
          FROM tramites.procedure_steps st
          JOIN tramites.procedure_types pt ON pt.id = st.procedure_type_id
         WHERE pt.code = 'TRASPASO_UNILATERAL'
           AND st.code = 'comprador'
           AND st.title = 'Locatario')
    THEN
        RAISE EXCEPTION 'TRASPASO_UNILATERAL: el paso de la parte entrante no quedó rotulado «Locatario».';
    END IF;
END $$;
