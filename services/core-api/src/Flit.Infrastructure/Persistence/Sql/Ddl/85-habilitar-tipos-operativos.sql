-- =============================================================================
-- ADR-0050 — enciende la barrera de operación de los tipos ya verificados.
--
-- `wizard_enabled` nació en false para los 21 tipos (DDL 79), y con razón: la
-- parametrización del resto del catálogo (DDL 82) es base técnica, no diseño
-- funcional validado con negocio. Pero MATRICULA_NUEVA y TRASPASO_STANDARD son
-- los dos recorridos que ya operaban antes de este trabajo y cuyos pasos vienen
-- del DDL 81, así que dejarlos apagados apagaría la operación entera.
--
-- Desde que la barrera se hace cumplir en el servidor (`procedure_type_not_enabled`
-- en CreateProcedureInstanceCommand), esto deja de ser cosmético: sin estas dos
-- filas en true no se puede crear NINGÚN trámite.
--
-- Solo se encienden si el tipo está publicado, activo y tiene pasos con secciones
-- — las mismas condiciones que exige el endpoint de habilitación. Un tipo a medio
-- parametrizar no se habilita ni siquiera desde aquí.
--
-- El resto del catálogo se habilita uno a uno, cuando su checklist esté cerrado,
-- con PUT /api/v1/superadmin/procedure-types/{id}/wizard-enabled.
-- =============================================================================

UPDATE tramites.procedure_types pt
SET wizard_enabled = true,
    updated_at = now()
WHERE pt.code IN ('MATRICULA_NUEVA', 'TRASPASO_STANDARD')
  AND pt.wizard_enabled = false
  AND pt.is_active = true
  AND pt.publication_status = 'published'
  AND EXISTS (
      SELECT 1
      FROM tramites.procedure_steps ps
      JOIN tramites.procedure_sections sec ON sec.procedure_step_id = ps.id
      WHERE ps.procedure_type_id = pt.id
        AND ps.deleted_at IS NULL
        AND sec.deleted_at IS NULL
  );

-- Si los dos canónicos quedaron apagados, la operación queda muerta y en silencio:
-- el selector no ofrece nada y toda creación responde 422. Mejor fallar el arranque
-- con el motivo que arrancar sin poder crear un trámite.
DO $$
DECLARE
    habilitados int;
BEGIN
    SELECT count(*) INTO habilitados
    FROM tramites.procedure_types
    WHERE code IN ('MATRICULA_NUEVA', 'TRASPASO_STANDARD')
      AND wizard_enabled = true;

    IF habilitados = 0 THEN
        RAISE EXCEPTION
            'Ningún tipo canónico quedó habilitado: revisa que MATRICULA_NUEVA y TRASPASO_STANDARD '
            'estén publicados, activos y con pasos parametrizados (DDL 81).';
    END IF;
END $$;
