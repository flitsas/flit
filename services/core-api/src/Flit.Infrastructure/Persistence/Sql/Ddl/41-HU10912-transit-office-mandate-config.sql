-- HU #10912 (ADR-0036) — Configuración de mandato por Organismo de Tránsito.
-- Regla PROPIA DEL OT (no del tenant): qué plantilla de mandato usa, si exige mandato también a
-- persona natural, y los datos del mandatario institucional (para OT con firmante fijo como Sabaneta
-- UT-SETSA o Bello UT-MAB). Llaveada por transit_office_id (catálogo RUNT, ON DELETE RESTRICT), SIN
-- tenant_id: la consumen todas las compañías gestoras que operan ese OT. Tabla DISPERSA: solo existen
-- filas para los OT configurados; ausencia de fila = plantilla genérica + mandato solo para persona
-- jurídica (comportamiento por defecto). template_code es un CHECK cerrado (mismo criterio que
-- 33-F05/34-F05): añadir una variante exige tocar el código del generador, así que un catálogo en BD
-- daría la falsa ilusión de que insertar una fila configura una plantilla nueva.
-- Sin RLS ni auditoría por fila (no hay tenant_id): solo el trigger de row_version para el control de
-- concurrencia. DDL IDEMPOTENTE (CREATE ... IF NOT EXISTS + guardas): re-aplicable sin efecto.

CREATE TABLE IF NOT EXISTS admin.transit_office_mandate_config (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_transit_office_mandate_config PRIMARY KEY (id),
    transit_office_id uuid NOT NULL UNIQUE
        REFERENCES catalogs.transit_offices(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    template_code varchar(30) NOT NULL DEFAULT 'generico'
        CONSTRAINT ck_transit_office_mandate_config_template
        CHECK (template_code IN ('generico', 'sabaneta', 'bello')),
    requires_for_natural_person boolean NOT NULL DEFAULT false,
    institutional_mandatary_name varchar(200),
    institutional_mandatary_nit varchar(20),
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);

DROP TRIGGER IF EXISTS tr_transit_office_mandate_config_row_version ON admin.transit_office_mandate_config;
CREATE TRIGGER tr_transit_office_mandate_config_row_version BEFORE UPDATE ON admin.transit_office_mandate_config
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();

-- Seed: Sabaneta (exige mandato también a PN; mandatario institucional UT-SETSA) y Bello (plantilla
-- propia; mandatario institucional UT-MAB, solo PJ). Idempotente por transit_office_id, matcheando el
-- OT por su código RUNT. Si el OT no existe en el catálogo, la fila no se inserta (sin efecto).
INSERT INTO admin.transit_office_mandate_config
    (transit_office_id, template_code, requires_for_natural_person, institutional_mandatary_name, institutional_mandatary_nit)
SELECT t.id, v.template_code, v.requires_pn, v.name, v.nit
FROM (VALUES
    ('5631000', 'sabaneta', true,  'UNION TEMPORAL SERVICIOS ESPECIALIZADOS DE TRANSITO Y TRANSPORTE DE SABANETA SETSA', '900273813-7'),
    ('5088000', 'bello',    false, 'UNION TEMPORAL MOVILIDAD AVANZADA DE BELLO MAB', '901783814-6')
) AS v(code, template_code, requires_pn, name, nit)
JOIN catalogs.transit_offices t ON t.code = v.code
ON CONFLICT (transit_office_id) DO NOTHING;
