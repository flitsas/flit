-- Plantilla de sistema `municipio` (redacción corta PN) para Envigado, Funza y Medellín.
-- Misma lógica que Sabaneta/Bello: el código RUNT del OT resuelve la redacción en runtime
-- vía MandatoSystemOfficeTemplates; este seed deja fila explícita cuando el OT existe en catálogo.
-- DDL IDEMPOTENTE.

INSERT INTO admin.transit_office_mandate_config
    (transit_office_id, template_code, requires_for_natural_person, mandatary_family, assignment_mode, chamber_city)
SELECT t.id, v.template_code, true, 'individuo', 'signer', v.city
FROM (VALUES
    ('5266000',  'municipio', 'Envigado'),
    ('25286000', 'municipio', 'Funza'),
    ('5001000',  'municipio', 'Medellín')
) AS v(code, template_code, city)
JOIN catalogs.transit_offices t ON t.code = v.code
ON CONFLICT (transit_office_id) DO NOTHING;
