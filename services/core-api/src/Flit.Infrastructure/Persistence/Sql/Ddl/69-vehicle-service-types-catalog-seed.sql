-- Siembra de los 6 tipos de servicio del vehículo (sección 18 del FUR).
-- Idempotente por `code` — re-ejecutar no duplica ni pisa lo que SuperAdmin haya editado después
-- (mismo criterio que 56-causales-rechazo-seed.sql: sin ON CONFLICT DO UPDATE).

INSERT INTO catalogs.vehicle_service_types (id, code, name, sort_order)
VALUES
    ('0198f1b0-0001-79d0-8000-000000000001'::uuid, 'PARTICULAR',  'Particular',   1),
    ('0198f1b0-0002-79d0-8000-000000000002'::uuid, 'PUBLICO',     'Público',      2),
    ('0198f1b0-0003-79d0-8000-000000000003'::uuid, 'DIPLOMATICO', 'Diplomático',  3),
    ('0198f1b0-0004-79d0-8000-000000000004'::uuid, 'OFICIAL',     'Oficial',      4),
    ('0198f1b0-0005-79d0-8000-000000000005'::uuid, 'ESPECIAL',    'Especial',     5),
    ('0198f1b0-0006-79d0-8000-000000000006'::uuid, 'OTROS',       'Otros',        6)
ON CONFLICT (code) DO NOTHING;
