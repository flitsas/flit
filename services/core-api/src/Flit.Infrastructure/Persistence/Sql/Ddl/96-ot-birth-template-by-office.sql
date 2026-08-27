-- HU-L10 — Plantilla de nacimiento por OT conocido.
-- Sabaneta/Bello/Envigado/Funza/Medellín: su redacción. Resto: no se toca (siguen en genérico).
-- No reescribe plantilla propia (pdf/editor) ni una elección distinta de 'generico'.
-- DDL IDEMPOTENTE.

INSERT INTO admin.transit_office_mandate_config
    (transit_office_id, template_code, requires_for_natural_person, mandatary_family,
     assignment_mode, institutional_mandatary_name, institutional_mandatary_nit,
     chamber_city, mandatary_sigla)
SELECT t.id, v.template_code, v.requires_pn, v.family, 'open',
       NULLIF(v.inst_name, ''), NULLIF(v.inst_nit, ''), NULLIF(v.city, ''), NULLIF(v.sigla, '')
FROM (VALUES
    ('5631000',  'sabaneta',  true, 'organismo_transito',
     'UNION TEMPORAL SERVICIOS ESPECIALIZADOS DE TRANSITO Y TRANSPORTE DE SABANETA SETSA',
     '900273813-7', 'Medellín', 'UT-SETSA'),
    ('5088000',  'bello',     true, 'organismo_transito',
     'UNION TEMPORAL MOVILIDAD AVANZADA DE BELLO MAB',
     '901783814-6', 'Medellín', ''),
    ('5266000',  'municipio', true, 'individuo', '', '', 'Envigado', ''),
    ('25286000', 'municipio', true, 'individuo', '', '', 'Funza', ''),
    ('5001000',  'municipio', true, 'individuo', '', '', 'Medellín', '')
) AS v(code, template_code, requires_pn, family, inst_name, inst_nit, city, sigla)
JOIN catalogs.transit_offices t ON t.code = v.code
ON CONFLICT (transit_office_id) DO NOTHING;

UPDATE admin.transit_office_mandate_config AS c
SET template_code = v.template_code,
    requires_for_natural_person = v.requires_pn,
    mandatary_family = v.family,
    institutional_mandatary_name = COALESCE(NULLIF(c.institutional_mandatary_name, ''), NULLIF(v.inst_name, '')),
    institutional_mandatary_nit = COALESCE(NULLIF(c.institutional_mandatary_nit, ''), NULLIF(v.inst_nit, '')),
    chamber_city = COALESCE(NULLIF(c.chamber_city, ''), NULLIF(v.city, '')),
    mandatary_sigla = COALESCE(NULLIF(c.mandatary_sigla, ''), NULLIF(v.sigla, '')),
    updated_at = now()
FROM catalogs.transit_offices AS t
JOIN (VALUES
    ('5631000',  'sabaneta',  true, 'organismo_transito',
     'UNION TEMPORAL SERVICIOS ESPECIALIZADOS DE TRANSITO Y TRANSPORTE DE SABANETA SETSA',
     '900273813-7', 'Medellín', 'UT-SETSA'),
    ('5088000',  'bello',     true, 'organismo_transito',
     'UNION TEMPORAL MOVILIDAD AVANZADA DE BELLO MAB',
     '901783814-6', 'Medellín', ''),
    ('5266000',  'municipio', true, 'individuo', '', '', 'Envigado', ''),
    ('25286000', 'municipio', true, 'individuo', '', '', 'Funza', ''),
    ('5001000',  'municipio', true, 'individuo', '', '', 'Medellín', '')
) AS v(code, template_code, requires_pn, family, inst_name, inst_nit, city, sigla)
  ON t.code = v.code
WHERE c.transit_office_id = t.id
  AND lower(c.template_code) = 'generico'
  AND coalesce(lower(c.custom_template_kind), 'none') = 'none';
