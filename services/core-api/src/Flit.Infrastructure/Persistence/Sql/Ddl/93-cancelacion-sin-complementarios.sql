-- =============================================================================
-- Cancelación de matrícula: sin trámites complementarios.
-- Migración: 20260826140000_CancelacionSinComplementarios (DDL 93)
--
-- `CANCELACION_MATRICULA` es de la familia MATRICULAS, y esa familia acumula trámites
-- complementarios (art. 5.1.8): por eso el asistente le pintaba «Asignación de Prenda / Limitación a
-- la Propiedad» y «Trámites Simultáneos — Transformaciones del Vehículo», igual que a una matrícula
-- inicial.
--
-- Pero acumular presupone un vehículo que SIGUE inscrito. La cancelación hace lo contrario: saca el
-- vehículo del registro. Inscribir una limitación a la propiedad sobre una matrícula que se está
-- cancelando —o declararle un cambio de color al vehículo que deja de circular— no son trámites
-- simultáneos, son contradicciones, y el organismo devuelve el FUR que las lleva.
--
-- El `gate_profile` por tipo existe justo para esto (DDL 87): la llave declarada MANDA sobre la
-- familia, y su ausencia significa «lo que diga la familia». Aquí se declara para el único tipo de
-- MATRICULAS que no acumula. Apagarlas cierra las dos secciones en el asistente
-- (WizardCapabilitiesDto) y también, en el servidor, el PATCH de las transformaciones y el PUT de la
-- decisión de prenda: no queda puerta trasera por API.
--
-- Los expedientes ABIERTOS conservan su perfil congelado en `procedure_type_snapshots`: un borrador
-- de cancelación creado antes de esto sigue mostrando las secciones hasta que se cierre. Es el mismo
-- criterio del DDL 87 y evita que a un trámite en curso le desaparezcan datos ya capturados.
--
-- Idempotente y reaplicable.
-- =============================================================================

UPDATE tramites.procedure_types
   SET gate_profile = coalesce(gate_profile, '{}'::jsonb)
                      || '{"allowsComplementaryTransformations": false, "allowsComplementaryPrenda": false}'::jsonb,
       updated_at = now()
 WHERE code = 'CANCELACION_MATRICULA'
   AND (gate_profile -> 'allowsComplementaryTransformations' IS DISTINCT FROM 'false'::jsonb
        OR gate_profile -> 'allowsComplementaryPrenda' IS DISTINCT FROM 'false'::jsonb);

-- Guarda: si el tipo existe, tiene que quedar declarado. Un fallo silencioso aquí deja las secciones
-- abiertas en el trámite que precisamente no puede llevarlas.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM tramites.procedure_types WHERE code = 'CANCELACION_MATRICULA')
       AND NOT EXISTS (
           SELECT 1
             FROM tramites.procedure_types
            WHERE code = 'CANCELACION_MATRICULA'
              AND gate_profile ->> 'allowsComplementaryTransformations' = 'false'
              AND gate_profile ->> 'allowsComplementaryPrenda' = 'false')
    THEN
        RAISE EXCEPTION 'CANCELACION_MATRICULA quedó admitiendo trámites complementarios';
    END IF;
END $$;
