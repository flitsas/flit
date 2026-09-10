-- =============================================================================
-- Enciende la barrera de operación de TRASPASO_UNILATERAL.
--
-- El tipo nació apagado (`wizard_enabled = false`, DDL 79) y así se quedó cuando DDL 85 encendió los
-- dos recorridos canónicos: su parametrización venía del seed técnico de DDL 82, que estaba al revés
-- de lo validado por negocio. ADR-0051 (DDL 94) corrigió esa parametrización —comparece el
-- propietario pero no se captura por formulario, solo él firma y valida identidad, no hay compraventa
-- ni avalúo— y dejó el recorrido completo: consulta → comprador → documentos → identidad → FUR.
--
-- Con la parametrización ya validada, el interruptor es lo único que separa al tipo de poder
-- operarse: sin él, la opción «Traspaso Unilateral» del modal de creación no puede resolverse a este
-- código.
--
-- Mismas condiciones que DDL 85, y por el mismo motivo: un tipo a medio parametrizar no se habilita
-- ni siquiera desde aquí. Publicado, activo y con pasos activos que tengan secciones.
--
-- Idempotente y reaplicable.
-- =============================================================================

UPDATE tramites.procedure_types pt
   SET wizard_enabled = true,
       updated_at = now()
 WHERE pt.code = 'TRASPASO_UNILATERAL'
   AND pt.wizard_enabled = false
   AND pt.is_active = true
   AND pt.publication_status = 'published'
   AND EXISTS (
       SELECT 1
         FROM tramites.procedure_steps ps
         JOIN tramites.procedure_sections sec ON sec.procedure_step_id = ps.id
        WHERE ps.procedure_type_id = pt.id
          AND ps.is_active
   );

-- Guarda: si el tipo EXISTE y quedó apagado, es que no cumple alguna de las condiciones de arriba
-- (despublicado, inactivo o sin secciones). Fallar aquí con el motivo es mejor que arrancar con la
-- opción del modal ofrecida y muerta.
--
-- La guarda no se dispara donde el tipo no existe: hay ambientes cuyo catálogo no lo tiene sembrado,
-- y para ellos esto es legítimamente un no-op.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM tramites.procedure_types WHERE code = 'TRASPASO_UNILATERAL')
       AND NOT EXISTS (
           SELECT 1
             FROM tramites.procedure_types
            WHERE code = 'TRASPASO_UNILATERAL'
              AND wizard_enabled = true)
    THEN
        RAISE EXCEPTION
            'TRASPASO_UNILATERAL no se pudo habilitar: verifica que esté publicado, activo y con '
            'pasos activos que tengan secciones (ver DDL 82 y 94).';
    END IF;
END $$;
