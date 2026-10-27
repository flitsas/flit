-- =============================================================================
-- HU #13641 (Feature #13100 FA-6, Épica #12753) — marca en el catálogo qué permisos exige la API.
-- Migración: 20261016190000_HU13641_PermisosEnforced.
--
-- security.permissions.enforced (text NOT NULL, CHECK, mismo patrón text+CHECK que publication_status/result):
--   'true'     la API (o core-ict / core-identity) exige el slug hoy.
--   'false'    el slug existe en el catálogo pero ninguna ruta lo exige todavía (default: un slug nuevo que nadie
--              clasifica no promete una protección que no existe).
--   'excluded' decisión de producto: no se exige ni se asigna por rol (usuarios.manage, rbac.manage, auth.me.read,
--              banners.manage — las dos primeras y banners son exclusivas de SuperAdmin; auth.me.read es base).
-- Esta lista es la fuente única: la leen el seeder de desarrollo (permisos recién sembrados) y el test de
-- arquitectura PermisosEnforcedArchitectureTests (sin marcador mentiroso: 'true' sin enforcement o 'false' exigido falla).
-- Idempotente: reaplicarlo reescribe los mismos valores. Los slugs que aún no existan se clasifican al sembrarse.
-- =============================================================================

ALTER TABLE security.permissions ADD COLUMN IF NOT EXISTS enforced text NOT NULL DEFAULT 'false';

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_permissions_enforced') THEN
        ALTER TABLE security.permissions
            ADD CONSTRAINT ck_permissions_enforced CHECK (enforced IN ('true', 'false', 'excluded'));
    END IF;
END $$;

UPDATE security.permissions p
   SET enforced = v.enforced
  FROM (VALUES
        ('dashboard.read', 'true'), ('tramites.read', 'true'), ('validaciones.read', 'true'), ('validaciones.manage', 'true'),
        ('ict.trazabilidad.read', 'true'), ('ict.reportes.read', 'true'), ('ict.clients.manage', 'true'), ('ict.pii.reveal', 'true'),
        ('runt_confirmation.settings.manage', 'true'), ('runt_confirmation.history.read', 'true'),
        ('generacion-documental.read', 'true'), ('generacion-documental.generate', 'true'),
        ('AdminTramiteCambiarEstado', 'true'), ('AdminTramiteAnular', 'true'), ('AdminTramiteLimpiarConsolidado', 'true'),
        ('AdminTramiteCargarConsolidado', 'true'), ('AdminTramiteReenviarValidacion', 'true'), ('AdminTramiteReasignarGestor', 'true'),
        ('logqx.read', 'true'), ('historial-placa.read', 'true'), ('consolidado-masivo.download', 'true'),
        ('security.users.reset_password', 'true'),
        ('ot.tramites.manage', 'true'), ('ot.improntas.manage', 'true'), ('ot.reportes.manage', 'true'), ('ot.mandatos.manage', 'true'),
        ('ot.mandatarios.manage', 'true'), ('ot.documentos.manage', 'true'), ('ot.usuarios.manage', 'true'), ('ot.integracion.manage', 'true'),
        ('usuarios.manage', 'excluded'), ('rbac.manage', 'excluded'), ('auth.me.read', 'excluded'), ('banners.manage', 'excluded'),
        ('tramites.create', 'false'), ('reportes.read', 'false'), ('improntas.read', 'false'), ('improntas.generate', 'false'),
        ('reportes.resumen.read', 'false'), ('reportes.operacion.read', 'false'), ('reportes.ot.read', 'false'),
        ('reportes.uso.read', 'false'), ('reportes.productividad.read', 'false'), ('reportes.consultas.read', 'false'),
        ('reportes.programacion.manage', 'false'), ('reportes.detallados.read', 'false'), ('reportes.detallados.export', 'false'),
        ('ict.logs.read', 'false')
       ) AS v(slug, enforced)
 WHERE p.slug = v.slug
   AND p.enforced IS DISTINCT FROM v.enforced;
