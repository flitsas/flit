-- HU #10659 (DEV) — Catálogo RUNT de organismos de tránsito (B11)
-- ⚠️  DEV/QA: alinea nombres reales del RUNT para auto-bind en traspaso.
-- Fuente: context/traffic_secretaries_example/traffic_secreataries.txt
-- Generado por: services/core-api/tools/generate-transit-offices-runt-seed.py
-- Filas: 298 (deduplicadas por nombre, excluye TEST)
-- Idempotente: UPSERT por catalogs.transit_offices.code (uq_transit_offices_code).
-- Los 6 UUID fijos (aaaaaaaa-…001–006) se conservan para E2E HU #10133.

BEGIN;

SET LOCAL row_security = off;

-- Migrar códigos ficticios de 5 dígitos (HU #10133) a traffic_agency_code RUNT (8 dígitos).
-- Conserva los UUID fijos referenciados por grants/E2E.
UPDATE catalogs.transit_offices SET code = '11001000' WHERE id = 'aaaaaaaa-0001-4000-8000-000000000001'::uuid AND code <> '11001000';
UPDATE catalogs.transit_offices SET code = '5001000' WHERE id = 'aaaaaaaa-0001-4000-8000-000000000002'::uuid AND code <> '5001000';
UPDATE catalogs.transit_offices SET code = '76001000' WHERE id = 'aaaaaaaa-0001-4000-8000-000000000003'::uuid AND code <> '76001000';
UPDATE catalogs.transit_offices SET code = '8001000' WHERE id = 'aaaaaaaa-0001-4000-8000-000000000004'::uuid AND code <> '8001000';
UPDATE catalogs.transit_offices SET code = '68001000' WHERE id = 'aaaaaaaa-0001-4000-8000-000000000005'::uuid AND code <> '68001000';
UPDATE catalogs.transit_offices SET code = '13001000' WHERE id = 'aaaaaaaa-0001-4000-8000-000000000006'::uuid AND code <> '13001000';

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '978fbaa4-fd88-5ef9-a4ab-100fbdb7f731'::uuid,
    '10000030',
    'CENTRO DE SERVICIOS DE MOVILIDAD CALLE 13',
    '11',
    '11001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'aaaaaaaa-0001-4000-8000-000000000001'::uuid,
    '11001000',
    'SECRETARIA DISTRITAL DE MOVILIDAD DE BOGOTA',
    '11',
    '11001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'aaaaaaaa-0001-4000-8000-000000000006'::uuid,
    '13001000',
    'DPTO ADTVO TTOyTTE DIST CARTAGENA',
    '13',
    '13001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd11cc704-03e1-5fab-9c03-8f26462f469c'::uuid,
    '13052000',
    'STRIA MCPAL TTEyTTO ARJONA',
    '13',
    '13052',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '81aa9125-7790-5924-9b41-2e4a0d949f59'::uuid,
    '13222000',
    'INSTITUTO MUNICIPAL DE TRANSITO Y TRANSPORTE DE CLEMENCIA BOLIVAR',
    '13',
    '13222',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '7057f642-1316-5131-a210-8c44c0c85ff2'::uuid,
    '13244000',
    'INSTITUTO DE MOVILIDAD DE EL CARMEN DE BOLIVAR',
    '13',
    '13244',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '6eb5b304-b170-5029-8a66-49a7cde030f3'::uuid,
    '13430000',
    'F. MCPAL TTOyTTE TERRESTRE MAGANGUE',
    '13',
    '13430',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a2c71553-2f96-5c7c-8dc1-03870c01cd83'::uuid,
    '13468000',
    'INSP TTO MOMPOX',
    '13',
    '13468',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '8f5ab33c-8211-5d2e-b337-c8a7a0f07803'::uuid,
    '13657000',
    'DPTO TTOyTTE MCPAL SAN JUAN DE NEPOMUCENO',
    '13',
    '13657',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a214e4d9-22fa-5b54-908d-b6736f63914e'::uuid,
    '13683000',
    'MUNICIPIO DE SANTA ROSA DEL SUR DE BOLIVAR',
    '13',
    '13688',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '25ec4f9e-b0ed-585d-9e5b-64ee7bf2c174'::uuid,
    '13683001',
    'SECRETARIA DE MOVILIDAD DEL DEPARTAMENTO DE BOLIVAR',
    '13',
    '13683',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'b00e0d60-3ab5-5275-a316-1c68859200aa'::uuid,
    '13836000',
    'STRIA TTOYTTE MCPAL TURBACO',
    '13',
    '13836',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '1ddb9438-33f5-5030-b7a2-07d391674b41'::uuid,
    '15001000',
    'STRIA DE TTOyTTE TUNJA',
    '15',
    '15001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '01ed4ec7-ec12-5147-a5ff-b6519907db8f'::uuid,
    '15176000',
    'STRIA MCPAL  TTOyTTE CHIQUINQUIRÁ',
    '15',
    '15176',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '27d51a49-edd5-5a89-b866-f8551f2d22f3'::uuid,
    '15204000',
    'ITBOY - DIST TTO No 1/COMBITA',
    '15',
    '15204',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'dee010c2-f38a-5127-9d9b-8622160886f0'::uuid,
    '15238000',
    'STRIA MCPAL TTOyTTE DUITAMA',
    '15',
    '15238',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '72d768b4-a649-50be-97b1-d5b64d25fc11'::uuid,
    '15299000',
    'STRIA TTOyTTE MCPAL GARAGOA',
    '15',
    '15299',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'bc23f60e-7bef-5c16-a835-132ee9d17d07'::uuid,
    '15322000',
    'ITBOY - DIST TTO No. 6/GUATEQUE',
    '15',
    '15322',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '7d5061de-86f3-5d01-808f-f8deb78aa650'::uuid,
    '15407000',
    'ITBOY - DIST TTO No. 10/VILLA DE LEYVA',
    '15',
    '15407',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a230efb3-7675-5d3f-9096-34467ca878a8'::uuid,
    '15455000',
    'ITBOY - DIST TTO No. 9/MIRAFLORES',
    '15',
    '15455',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '6f1eb8ac-f99d-509a-8be4-6b711b73bd63'::uuid,
    '15469000',
    'ITBOY - DIST TTO No. 5/MONIQUIRA',
    '15',
    '15469',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'dfcfb93d-70e0-5bdc-b388-1880838e4e37'::uuid,
    '15491000',
    'ITBOY - DIST TTO No. 2/ NOBSA',
    '15',
    '15491',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'b051a3bf-014e-55b6-97f9-daaa2936f147'::uuid,
    '15516000',
    'STRIA TTOyTTE MCPAL PAIPA',
    '15',
    '15516',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'ff2647cd-3452-5dc3-a980-3d8d7166859b'::uuid,
    '15572000',
    'INSP TTOyTTE MCPAL PUERTO BOYACA',
    '15',
    '15572',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'cdb7efc9-1b54-515c-81dc-f23556f344ed'::uuid,
    '15599000',
    'ITBOY - DIST TTO No. 11/RAMIRIQUI',
    '15',
    '15599',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '0b00c783-de15-58ca-8249-fd9a5368ab6a'::uuid,
    '15632000',
    'ITBOY - DIST TTO No. 4/SABOYA',
    '15',
    '15632',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd0d0b4c8-8d1d-525e-bc8e-f353da6b446c'::uuid,
    '15693000',
    'ITBOY - DIST DE TTO SANTA ROSA DE VITERBO',
    '15',
    '15693',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '3ea0e6b1-3cdd-523b-9e3b-b75105ee55f3'::uuid,
    '15753000',
    'ITBOY - DIST TTO No. 7/SOATA',
    '15',
    '15753',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '384136d5-db05-55a3-a9a7-cb436d41d2bd'::uuid,
    '15759000',
    'INST TTOyTTE MCPAL SOGAMOSO',
    '15',
    '15759',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '31991383-8efb-5743-837b-853d4c991ff9'::uuid,
    '17001000',
    'SECRETARÍA DE MOVILIDAD DE MANIZALES',
    '17',
    '17001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '037abacf-5f44-5cbd-8fe2-c4e305d09b67'::uuid,
    '17013000',
    'INSP TTEyTTO AGUADAS',
    '17',
    '17013',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '1fe25759-acd8-5303-82a1-89a251dfb319'::uuid,
    '17042000',
    'STRIA TTEyTTO ANSERMA',
    '17',
    '17042',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f16d5054-358f-5c8d-9d7b-434129814f0c'::uuid,
    '17050000',
    'UND TTO CALDAS/ARANZAZU',
    '17',
    '17050',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '03adccf3-4194-5bd5-a51a-cffab614bfcf'::uuid,
    '17174000',
    'STRIA MCPAL TTOyTTE CHINCHINA',
    '17',
    '17174',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f3dd68b3-f02d-51f4-85de-05d485486c6a'::uuid,
    '17380000',
    'INPS TTOYTTE LA DORADA',
    '17',
    '17380',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '3789e793-e3c5-5338-b6c8-b316fbc5170e'::uuid,
    '17433000',
    'DIR MCPAL TTOyTTE MANZANARES',
    '17',
    '17433',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '0a28e48e-7eb3-5005-bc90-8bf248a38026'::uuid,
    '17486000',
    'STRIA TTO MOV MCPAL NEIRA/CALDAS',
    '17',
    '17486',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '8749ef16-9e94-5492-927f-a283167d5e10'::uuid,
    '17614000',
    'SUB STRIA MOVILIDAD RIOSUCIO',
    '17',
    '17614',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f23ccc8a-2c57-5b6f-b0d0-85b981b7c287'::uuid,
    '17653000',
    'STRIA TTOyTTE SALAMINA',
    '17',
    '17653',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '9c72d4a3-ad80-531a-b440-b1b2cafce305'::uuid,
    '17777000',
    'STRIA MOV MCPAL SUPIA/CALDAS',
    '17',
    '17777',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '92f0d4b7-fd48-523d-a86e-5e6b40832c67'::uuid,
    '17873000',
    'UNIDAD TTO CALDAS/VILLAMARIA',
    '17',
    '17873',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '84a58c8c-e346-5ea0-a4b7-beb3cb69a220'::uuid,
    '17877000',
    'STRIA MOV MCPAL VITERBO/CALDAS',
    '17',
    '17877',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd5bef58e-0f2c-564a-882b-51f82716f8c5'::uuid,
    '18001000',
    'STRIA TTOyTTE MCPAL FLORENCIA',
    '18',
    '18001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '078a44a9-4e12-5aad-9261-f0c98ba07a6e'::uuid,
    '18094000',
    'DIR TTOyTTE DPTAL CAQUETA/BELEN DE LOS ANDAQUIES',
    '18',
    '18094',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'b7c4ee84-24d0-5a88-8f6c-0cdbee201339'::uuid,
    '18256000',
    'DIR TTOYTTE DPTAL CAQUETA/EL PAUJIL',
    '18',
    '18256',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '4d018af6-8c56-5ff4-a3ac-7784ef0b10ea'::uuid,
    '18753000',
    'DIR TTOyTTE DPTAL CAQUETA/SAN VIC CAGUAN',
    '18',
    '18753',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '4e87f8c8-b0f3-5a60-8a7b-25670eb907ef'::uuid,
    '19001000',
    'STRIA TTOyTTE MCPAL POPAYAN',
    '19',
    '19001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '4a46222e-b98c-51ee-9822-4c2c0aa4834e'::uuid,
    '19100000',
    'STRIA TTOyTTE MCPAL BOLIVAR',
    '19',
    '19100',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '50f57f60-962e-5c85-96db-aaf5a81a0f1d'::uuid,
    '19142000',
    'STRIA TTOyTTE MCPAL CALOTO',
    '19',
    '19142',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '649d316f-ac21-5b3e-b159-af46bebbd144'::uuid,
    '19256000',
    'STRIA TTOYTTE MCPAL EL TAMBO',
    '19',
    '19256',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'ebb3bb3b-59c1-595d-9c76-f5503f0cc50e'::uuid,
    '19455000',
    'STRIA TTOyTTE MCPAL MIRANDA',
    '19',
    '19455',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd401b4a1-a657-59f2-9e80-9c49597df7d8'::uuid,
    '19532000',
    'STRIA TTO MCPAL PATIA',
    '19',
    '19532',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd02df8ea-9830-5a19-9653-ffb904513ebe'::uuid,
    '19548000',
    'STRIA TTOyTTE MCPAL PIENDAMO',
    '19',
    '19548',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '0cfa69f0-a230-57fb-b570-8f245ce28fe7'::uuid,
    '19573000',
    'STRIA TTO MCPAL PUERTO TEJADA',
    '19',
    '19573',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd6317c96-aecb-5d57-b892-1f6f3e9924ad'::uuid,
    '19698000',
    'STRIA TTOyTTE MPAL SANTANDER QUILICHAO',
    '19',
    '19698',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd92b4cce-7d8a-57a7-a764-47d9fcde50b5'::uuid,
    '19807000',
    'STRIA TTOyTTE MCPAL TIMBIO',
    '19',
    '19807',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f8754301-bc3f-5727-a1c3-b491d79f402c'::uuid,
    '19845000',
    'STRIA DE MOVILIDAD DEL MUNICIPIO DE VILLA RICA CAUCA',
    '19',
    '19845',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '53f89618-56b1-596b-a831-77e08f566094'::uuid,
    '20001000',
    'INST MCPAL TTOyTTE VALLEDUPAR',
    '20',
    '20001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '089c8500-d58f-50b1-9331-ec0c72aa97d2'::uuid,
    '20011000',
    'INST MCPAL TTOyTTE AGUACHICA',
    '20',
    '20011',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '2a66d203-b085-52bb-b6e9-8e6260ac2ea8'::uuid,
    '20013000',
    'STRIA TTEyTTO MCPAL AGUSTIN CODAZZI',
    '20',
    '20013',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '7683ddef-444d-5803-88d9-7969816b9a31'::uuid,
    '20060000',
    'STRIA MCPAL TTOyTTE BOSCONIA',
    '20',
    '20060',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '01503ffc-e97a-5e20-a4f7-7fcdaa435f68'::uuid,
    '20228000',
    'STRIA DE TTOyTTE MCPAL CURUMANI',
    '20',
    '20228',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'c5c0a4c7-16e7-50db-a82a-a857515b1e35'::uuid,
    '20250000',
    'STRIA DE TTO y TTE DE EL PASO-CESAR',
    '20',
    '20250',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '3d35e464-7d36-5157-a0c8-9dfb6898fce6'::uuid,
    '20400000',
    'MUNICIPIO DE LA JAGUA DE IBIRICO',
    '20',
    '20400',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '1c0afe7f-3d8d-5242-8d00-2bbd74f5fd92'::uuid,
    '20621000',
    'STRIA TTOyTTE MCPAL LA PAZ',
    '20',
    '20621',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'ef5146f5-7519-51d2-9c99-37ec81373844'::uuid,
    '20710001',
    'INST DTAL DE TTO CESAR/SAN ALBERTO',
    '20',
    '20710',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '711f5296-9072-5532-be22-c6b0888b7785'::uuid,
    '20750000',
    'INST DTAL DE TTO CESAR/SAN DIEGO',
    '20',
    '20750',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'ecca5475-5191-562e-8bb1-dc3745982c81'::uuid,
    '20770000',
    'STRIA TTOyTTE MCPAL SAN MARTÍN/CESAR',
    '20',
    '20770',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f19d5780-8807-5508-9615-c20914e3ee84'::uuid,
    '23001000',
    'STRIA MCPAL TTEyTTO MONTERIA',
    '23',
    '23001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '0c053c34-6abf-5ceb-97b9-a5da0d72ff96'::uuid,
    '23162000',
    'INST TTOyTTE DE CERETE',
    '23',
    '23162',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '39a36071-04f0-5e45-b0b3-76a4f7ee7397'::uuid,
    '23182000',
    'STRIA DPTAL TTOyTTE CORDOBA/CHINU',
    '23',
    '23182',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'e34dc666-014b-5e00-b820-ac1cc91b802d'::uuid,
    '23350000',
    'STRIA DPTAL TTOyTTE CORDOBA/APARTADA',
    '23',
    '23350',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '8d3c67f1-0fab-5a8d-9015-84a33ccb566a'::uuid,
    '23417000',
    'INSP TTOyTTE LORICA',
    '23',
    '23417',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'df2a301c-a7d2-55de-8f3a-785c3379ed30'::uuid,
    '23555000',
    'STRIA MCPAL TTEyTTO PLANETA RICA',
    '23',
    '23555',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a3f6bc69-579b-51f7-8144-920ab23cd0ac'::uuid,
    '23660000',
    'INSP TTOyTTE MCPAL SAHAGUN',
    '23',
    '23660',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '49ec6a2c-b96e-51fa-9e09-681984bef626'::uuid,
    '25053000',
    'STRIA TTEy MOV CUND/ARBELAEZ',
    '25',
    '25053',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '1ea9c4e4-ba95-5a7c-ba23-c89b3e0c5e89'::uuid,
    '25126000',
    'STRIA TTO TTEyMOV MCPAL CAJICA/CUND',
    '25',
    '25126',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '5edb4379-0d5b-541d-8e7f-333e6fc55558'::uuid,
    '25126001',
    'STRIA DPTAL TTEyTTO CUND/CAJICA',
    '25',
    '25126',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd8b2e481-9228-5ee2-a2ed-fe4263fe829e'::uuid,
    '25151000',
    'STRIA TTEy MOV CUND/CAQUEZA',
    '25',
    '25151',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '1436197e-2c35-5a6a-bc1b-c3bb4f3abb96'::uuid,
    '25175000',
    'SECRETARIA DE  MOVILIDAD MUNICIPAL DE CHIA',
    '25',
    '25175',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd58b29d8-ff94-5630-8ee3-213d1acf0401'::uuid,
    '25178000',
    'STRIA TTEyMOV CUND/CHIPAQUE',
    '25',
    '25178',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '16e2bd5c-700e-5c6d-bb2f-a054e161ca0d'::uuid,
    '25183000',
    'STRIA TTEyMOV CUND/CHOCONTA',
    '25',
    '25183',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '66283b7e-c2c1-5ed7-a791-1613f257d684'::uuid,
    '25214000',
    'STRIA TTEyMOV CUNDINAMARCA/COTA',
    '25',
    '25214',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'b9b0ac25-832d-5f11-a5fb-357720a6dbfd'::uuid,
    '25260000',
    'STRIA TTEyMOV CUND/EL ROSAL',
    '25',
    '25260',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a597849d-d83d-57a8-9d40-4f29a4925ab3'::uuid,
    '25269000',
    'STRIA TTO MCPAL FACATATIVA',
    '25',
    '25269',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'eeacc872-a522-56bb-9150-70776b094009'::uuid,
    '25286000',
    'STRIA TTOyTTE MCPAL FUNZA',
    '25',
    '25286',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '2dfd7fd4-af51-5d35-b0bf-948083920e3e'::uuid,
    '25290000',
    'STRIA DE MOVILIDAD MPAL FUSAGASUGA',
    '25',
    '25290',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '5fe4b6bb-fbcc-536e-83c6-69ee6f87f860'::uuid,
    '25307000',
    'STRIA TTOyTTE MCPAL GIRARDOT',
    '25',
    '25307',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '51480198-944c-5b4e-af90-927dc0ca501a'::uuid,
    '25320000',
    'STRIA TTOyTTE MCPAL DE GUADUAS',
    '25',
    '25320',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '9549a469-1745-5677-904d-416a77307e99'::uuid,
    '25377000',
    'STRIA TTOyTTE MCPAL LA CALERA/CUND',
    '25',
    '25377',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '3438fa51-fcb0-5fc1-9a77-15c2c0bc0717'::uuid,
    '25377001',
    'STRIA TTEyMOV CUND/LA CALERA',
    '25',
    '25377',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '98a25dcc-465b-5758-82ed-26bc1016fdc1'::uuid,
    '25386000',
    'SECRETARIA DE TRÁNSITO-DEL MUNICIPIO DE LA MESA-CUNDINAMARCA',
    '25',
    '25386',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'c67c2786-560d-541d-bf7c-01f8c26ddbdf'::uuid,
    '25430000',
    'STRIA TTOYTTE MCPAL DE MADRID',
    '25',
    '25430',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '7081d86b-70a5-59e3-b319-2340a20603d6'::uuid,
    '25473000',
    'SECRETARÍA DE MOVILIDAD DEL MUNICIPIO DE MOSQUERA',
    '25',
    '25473',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '91b32b59-4b59-5561-bd02-3527d482d10d'::uuid,
    '25513000',
    'STRIA TTOyTTE MCPAL PACHO',
    '25',
    '25513',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'b466eaf5-2005-5878-9f34-5335e3b98c9b'::uuid,
    '25572000',
    'STRIA TTEyMOV CUND/PUERTO SALGAR',
    '25',
    '25572',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '70e0c0b7-a6d7-5958-bb24-033b29a0f62c'::uuid,
    '25612000',
    'STRIA TTEyMOV CUND/RICAURTE',
    '25',
    '25612',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'bf308ae8-9683-5b8e-82e0-9bd71ca55801'::uuid,
    '25740000',
    'STRIA TTEy MOV CUND/SIBATE',
    '25',
    '25740',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd730ed80-7b79-55ff-b232-8e33029c26be'::uuid,
    '25743000',
    'SECRETARIA DE TRANSITO Y TRANSPORTE MUNICIPIO DE SILVANIA',
    '25',
    '25743',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '91349bb1-1785-52e2-aac1-044d3de5c0a2'::uuid,
    '25754000',
    'STRIA MCPAL DE SOACHA',
    '25',
    '25754',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '1d85179b-cdb5-5dc2-af79-05c7ea7f0a87'::uuid,
    '25758000',
    'STRIA TTOyMOV MCPAL SOPO/CUND',
    '25',
    '25758',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd0e00d9f-13cc-5d7f-8d62-448fc163fac6'::uuid,
    '25843000',
    'STRIA TTOyTTE MCPAL UBATE',
    '25',
    '25843',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'dab116f8-7777-5e0c-91bc-e10b3c75d906'::uuid,
    '25875000',
    'STRIA TTEyMOV CUND/VILLETA',
    '25',
    '25875',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '3d438442-5dac-5aa4-b91c-ee40dea8c792'::uuid,
    '25899000',
    'SECRETARIA DE TRANSPORTE Y MOVILIDAD DE ZIPAQUIRA',
    '25',
    '25899',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd5fabaff-dbd5-5505-bc45-178a51b6875d'::uuid,
    '27001000',
    'STRIA MOVyTTO QUIBDO',
    '27',
    '27001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '30419835-e9af-5cad-be8b-0ea7fe304f20'::uuid,
    '27361000',
    'DIR TTEyTTO DPTAL CHOCO/ISTMINA',
    '27',
    '27361',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '0d522f52-0b64-56a4-ab72-11bcaf944d0c'::uuid,
    '41001000',
    'SECRETARIA DE MOVILIDAD DE NEIVA',
    '41',
    '41001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'b8e0ee97-1369-50ce-8d60-481414d22539'::uuid,
    '41016000',
    'STRIA TTOyTTE MCPAL AIPE/HUILA',
    '41',
    '41016',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '7c5e7793-5e69-5923-b680-903560733e6b'::uuid,
    '41132000',
    'INST DE TTOyTTE DE CAMPOALEGRE HUILA ITTC',
    '41',
    '41132',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'da751c4c-aa3f-5517-b8bc-490b3147a2b9'::uuid,
    '41298000',
    'STRIA MCPAL TTOyTTE GARZON',
    '41',
    '41298',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'ce93c9bd-e932-58c7-adf0-e9dc035a4f76'::uuid,
    '41319000',
    'STRIA INFR PROD TTOyTTE MPAL GUADALUPE',
    '41',
    '41319',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '3d4e8266-7749-52ab-891f-068bcc9bfdee'::uuid,
    '41396000',
    'STRIA MCPAL TTOyTTE LA PLATA',
    '41',
    '41396',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '382ba6ef-04a6-52a6-bcc0-40dd0185322b'::uuid,
    '41524000',
    'UND MCPAL TTOyTTE PALERMO',
    '41',
    '41524',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '31076133-408c-5573-abf1-2418fb334bb3'::uuid,
    '41551000',
    'INST TTOyTTE DE PITALITO - INTRAPITALITO',
    '41',
    '41551',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'cf39f146-0398-549d-a883-3762d60f0bf2'::uuid,
    '41615000',
    'INSTITUTO DE TRANSPORTES Y TRÁNSITO DEL HUILA/RIVERA',
    '41',
    '41615',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'c3534643-8858-5b94-81dc-ca18979fa4b4'::uuid,
    '41807000',
    'INST TTOyTTE DPTAL HUILA/TIMANA',
    '41',
    '41807',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd69cc7a9-a600-54c6-aba8-600af897af01'::uuid,
    '44001000',
    'INST. DE TT0, TTE y MOVILIDAD DISTRITAL DE RIOHACHA',
    '44',
    '44001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '6f67763a-fd24-59e8-846a-79ba06770d2b'::uuid,
    '44035000',
    'INST TTOYTTE DEL MUNICIPIO DE ALBANIA',
    '44',
    '44035',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '2796c7b1-8798-5e6b-ad83-60cd3273ddb7'::uuid,
    '44279000',
    'INSTITUTO DE TRANSITO Y TRANSPORTE DE FONSECA LA GUAJIRA',
    '44',
    '44279',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '18ce2a78-2cba-5bc6-b21e-303c7ed12241'::uuid,
    '44378000',
    'DPTO ADTVO TTOyTTE GUAJIRA/HATONUEVO',
    '44',
    '44378',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f8491717-df50-5a66-b27f-ace468d02af2'::uuid,
    '44430000',
    'INST MCPAL TTOyTTE MAICAO',
    '44',
    '44430',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '3a607aed-da7d-5336-9a21-d6bda1d6be59'::uuid,
    '47001000',
    'U TEC CONT/VIG/ REG TTOyTTE SANTA MARTA',
    '47',
    '47001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'aadb5f51-da47-5f12-9365-434302029291'::uuid,
    '47053000',
    'INST MCPAL TTO DE ARACATACA - IMTARAC',
    '47',
    '47053',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '41832d26-f1e8-539a-a970-e2dcc19b1ace'::uuid,
    '47189000',
    'INST TTOyTTE MCPAL CIENAGA',
    '47',
    '47189',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '24622e93-a3f5-503f-bbf1-3df1c8d5f02c'::uuid,
    '47245000',
    'INST MCPAL TTOyTTE EL BANCO',
    '47',
    '47245',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '8eafd472-da35-58c9-97a9-7688aab2188a'::uuid,
    '47288000',
    'INST MCPAL TTOyTTE FUNDACION',
    '47',
    '47288',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'e01ec60d-f24a-5b6e-bfaf-6661970e7426'::uuid,
    '47555000',
    'STRIA TTOyTTE MCPAL PLATO - MAGDALENA',
    '47',
    '47555',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'fb8f1a5a-e53d-5f51-b4aa-8e8abb6bfd35'::uuid,
    '47745000',
    'TRÁNSITO DPTAL DEL MAGDALENA/SITIONUEVO',
    '47',
    '47745',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '38da7c7b-3a1a-5f1e-848c-70287bf1fbb5'::uuid,
    '47980000',
    'STRIA TTOyTTE MCPAL ZONA BANANERA MAGDALENA',
    '47',
    '47980',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'cd50ac67-d6e9-5ac5-8ac7-0438ba54e421'::uuid,
    '50001000',
    'STRIA TTOyTTE MCPAL VILLAVICENCIO',
    '50',
    '50001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '077c9bf5-9b9b-50f0-b47d-5ad39feaeb90'::uuid,
    '50006000',
    'INST TTOyTTE ACACIAS',
    '50',
    '50006',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'aaaaaaaa-0001-4000-8000-000000000002'::uuid,
    '5001000',
    'STRIA DE TTOyTTE MEDELLIN',
    '05',
    '05001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'ecfed340-51b4-5f9d-bf95-787ac2f836f4'::uuid,
    '5031000',
    'STRIA TTOyTTE MCPAL AMALFI',
    '05',
    '05031',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '4aaa6788-d12b-509a-aee0-eef9795c07c5'::uuid,
    '50313000',
    'STRIA MCPAL TTO GRANADA',
    '50',
    '50313',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd3fda703-f116-5636-98a5-16f7a8b81ac4'::uuid,
    '50318000',
    'INST DPTAL TTOyTTE META/GUAMAL',
    '50',
    '50318',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'fb8a4e75-556a-5b96-a0bc-1dc687b1a7a9'::uuid,
    '5034000',
    'INSP TTOyTTE ANDES',
    '05',
    '05034',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '345a58b1-029c-56a3-9c68-61ca2159e031'::uuid,
    '5042000',
    'STRIA TTEyTTO MCPAL SANTA FE ANTIOQUIA',
    '05',
    '05042',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '740c4c48-5a0f-5ba5-ab5c-c8f1b439a3fe'::uuid,
    '5045000',
    'STRIA MCPAL TTEyTTO APARTADO',
    '05',
    '05045',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '7b4609c5-a816-540b-a923-041432ebce47'::uuid,
    '50573000',
    'INST DPTAL TTOyTTE META/PUERTO LOPEZ',
    '50',
    '50573',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '91e3b769-4bc0-582f-94d9-5be94b3c0559'::uuid,
    '50606000',
    'INST DPTAL TTOyTTE META/RESTREPO',
    '50',
    '50606',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'dd68d155-3b43-56d4-b93c-aeb18fbaf0da'::uuid,
    '50606001',
    'INST DPTAL TTOyTTE  META/RESTREPO',
    '50',
    '50606',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '6e96c215-2443-5666-8ed6-c4749ac3861c'::uuid,
    '5079000',
    'DIR TTEyTTO  MCPAL BARBOSA',
    '05',
    '05079',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '8cef5e91-c23c-5092-83e6-94306cdc7644'::uuid,
    '5088000',
    'STRIA TTEyTTO BELLO',
    '05',
    '05088',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '1074b370-9fcc-5d50-ad01-2603ec4bbf40'::uuid,
    '5101000',
    'INSP MCPAL TTO CIUDAD BOLIVAR',
    '05',
    '05101',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'beffba04-045f-5ab0-b283-c60aa7bb9a2c'::uuid,
    '5129000',
    'STRIA TTEyTTO MCPAL CALDAS/ANTIOQUIA',
    '05',
    '05129',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '65da6c5d-2273-56dd-8657-bf6145cdbb15'::uuid,
    '5147000',
    'STRIA DE TTOyTTE CAREPA',
    '05',
    '05147',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '5adb92c2-156f-5467-8a1f-fd03cd23dbbc'::uuid,
    '5148000',
    'INSP MCPAL TTOyTTE CARMEN DE VIBORAL',
    '05',
    '05148',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'ade9f38b-a256-573f-af9c-cc25fca972d4'::uuid,
    '5154000',
    'STRIA TTOYTTE CAUCASIA',
    '05',
    '05154',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '99a0bec6-a9da-578b-a6a1-73dc57775a4b'::uuid,
    '5172000',
    'SECRETARÍA DE TTOYTTE MCPAL CHIGORODO',
    '05',
    '05172',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '4682015f-d5e3-57ea-801f-e463e0a0fd2e'::uuid,
    '52001000',
    'DPTO ADTVO TTOYTTE MCPAL PASTO',
    '52',
    '52001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '777ac311-8ad1-5219-9ef0-47edb412fb27'::uuid,
    '52110000',
    'STRIA TTOyTTE MCPAL BUESACO/NARIÑO',
    '52',
    '52110',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd640174d-e9d2-5e13-a05d-251ce8e79894'::uuid,
    '52110001',
    'SUBSTRIA TTOyTTE DPTAL NARIÑO/BUESACO',
    '52',
    '52110',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a7c180ce-2267-56f7-8287-391350f1040a'::uuid,
    '5212000',
    'STRIA TTEyTTO COPACABANA',
    '05',
    '05212',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '514159d2-a093-5dcc-97aa-26ae3ed942a0'::uuid,
    '52240000',
    'STRIA TTOyTTE MCPAL CHACHAGUI NARIÑO',
    '52',
    '52240',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'c6841e1b-df59-52a7-9351-466ae183e7e9'::uuid,
    '52317000',
    'SUBSTRIA TTOyTTE DTAL NARIÑO/GUACHUCAL',
    '52',
    '52317',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'e9649d3c-b69f-516f-af03-02e0492795d5'::uuid,
    '52354000',
    'SUB STARIA TTOyTTE DPTAL NARIÑO/IMUES',
    '52',
    '52354',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a1deab26-3fc6-5d95-ad78-4e454cae8bbe'::uuid,
    '52356000',
    'STRIA TTOyTTE MCPAL IPIALES',
    '52',
    '52356',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'c0fece49-7c79-5410-8236-4738e315a161'::uuid,
    '5237000',
    'DIR TTO MCPAL DONMATIAS',
    '05',
    '05237',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'fca23c58-23ee-5bfa-9a5d-73173a36a029'::uuid,
    '52399000',
    'SUBSTRIA TTOyTTE DPTAL NARIÑO/LA UNION',
    '52',
    '52399',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '08749895-62c8-5844-a0fa-1f34b0372ea9'::uuid,
    '52480000',
    'STRIA TTOYTTE MCPAL NARIÑO',
    '52',
    '52480',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '1f7fe317-b65f-5830-a3b8-46b262a03e28'::uuid,
    '5250000',
    'STRIA TTOyTTE DEL MCPIO DEL BAGRE',
    '05',
    '05250',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a75a2976-315b-5633-8a37-f3f418d117bd'::uuid,
    '52585000',
    'SUBSTRIA TTOyTTE DPTAL NARIÑO/PUPIALES',
    '52',
    '52585',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '708488b4-111e-54c1-aebf-2900a472f609'::uuid,
    '52612000',
    'STRIA TTOYTTE MCPAL RICAURTE',
    '52',
    '52612',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '69f48545-a7cf-5201-9198-6e3b3fab9a99'::uuid,
    '5266000',
    'STRIA TTEyTTO ENVIGADO',
    '05',
    '05266',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '90a6823c-b815-565b-93ab-66016cbd4b2d'::uuid,
    '52678000',
    'SUBSTRIA TTOyTTE DPTAL NARIÑO/SAMANIEGO',
    '52',
    '52678',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'b57a118a-6a12-5c63-9da5-9ae00d03c217'::uuid,
    '52683000',
    'SUBSTRIA TTOyTTE DPTAL NARIÑO/SANDONA',
    '52',
    '52683',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '1820b710-7d78-56f6-8620-70bcd7a49358'::uuid,
    '52788000',
    'STRIA TTOyTTE DPTAL TANGUA/NARIÑO',
    '52',
    '52788',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '4cdb43da-0ff4-576c-88a6-98989102d60d'::uuid,
    '52835000',
    'STRIA TTEyTTO MCPAL TUMACO',
    '52',
    '52835',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd0a75b85-10d3-59ca-a47a-c7eec8471c54'::uuid,
    '52838000',
    'STRIA TT0yTTE MCPAL TUQUERRES',
    '52',
    '52838',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'db7a04a4-be38-5906-83d1-5214037b54eb'::uuid,
    '5284000',
    'STRIA TTEyTTO MCPAL FRONTINO',
    '05',
    '05284',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd7894e55-8f77-5cfb-a885-2103ed441560'::uuid,
    '52885001',
    'TRÁNSITO DPTAL NARIÑO/YACUANQUER',
    '52',
    '52885',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '4bdcdc26-ba63-5600-994a-32f1f4926147'::uuid,
    '5308000',
    'STRIA TTEyTTO GIRARDOTA',
    '05',
    '05308',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '5b9af975-748c-53a5-bac0-6b5ab8f8001c'::uuid,
    '5318000',
    'STRIA DE MOVILIDAD DEL MCPIO DE GUARNE',
    '05',
    '05318',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd31bc0e1-738d-5973-94d0-45d36f928c88'::uuid,
    '5360000',
    'STRIA TTEyTTO ITAGUI',
    '05',
    '05360',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '304acea5-7a61-5043-82df-465899d3c80c'::uuid,
    '5376000',
    'INSP TTO MCPAL LA CEJA',
    '05',
    '05376',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'df8637d6-eafd-5514-8876-b4a0785d808e'::uuid,
    '5380000',
    'STRIA TTOYTTE MCPAL LA ESTRELLA',
    '05',
    '05380',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f19a2cdf-c1b3-55a2-a452-dc716caa9072'::uuid,
    '54001000',
    'DPTO ADTVO TTOyTTE MCPAL CUCUTA',
    '54',
    '54001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '0b03729e-88a2-5d2a-bb04-635d85aa00a3'::uuid,
    '54206000',
    'INSP DE TT0 y TTE CONVENCION',
    '54',
    '54206',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f956c17a-9395-5e32-a78b-776865cd5022'::uuid,
    '54261000',
    'STRIA TTO DPTAL NTE SANTANDER/EL ZULIA',
    '54',
    '54261',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '7d3933dd-475e-565c-a6ef-bdde107a4846'::uuid,
    '5440000',
    'STRIA DE TTOYTTE DEL MCPIO DE MARINILLA',
    '05',
    '05440',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '6da391e3-f747-578d-9a56-7c16c2ce52fb'::uuid,
    '54405000',
    'INST TTOyTTE DEL MUNICIPIO DE LOS PATIOS',
    '54',
    '54405',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '4f73ee22-bfc9-5630-928f-6cd9675e3dee'::uuid,
    '54498000',
    'STRIA MOVyTTO OCAÑA',
    '54',
    '54498',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'c48ac782-c693-5321-bb64-46614df06cb6'::uuid,
    '54518000',
    'STRIA MCPAL TTOyTTE PAMPLONA',
    '54',
    '54518',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '96b9fb82-0f2d-5052-8807-8bc220e90ff2'::uuid,
    '54874000',
    'DPTO ADTVO TTEyTTO VILLA DEL ROSARIO',
    '54',
    '54874',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '8cca2f5a-3520-50f0-a598-6316a4d091d3'::uuid,
    '5490000',
    'STRIA TTOyTTE MCPAL NECOCLÍ/ANTIOQUIA',
    '05',
    '00549',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '169883ba-90cc-5392-9125-23a88ab008bd'::uuid,
    '5579000',
    'INSP TTO PUERTO BERRIO',
    '05',
    '05579',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '66b24fb8-538f-5a7d-bfa1-9a35bbe055fa'::uuid,
    '5591000',
    'STRIA TTOyTTE MCPAL DE PUERTO TRIUNFO',
    '05',
    '05591',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a03222b8-627d-5940-a1db-7b1f3eba322b'::uuid,
    '5615000',
    'SDT SUBSECRETARÍA DE MOVILIDAD RIONEGRO',
    '05',
    '05615',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'ba575641-ea48-5cd2-ac51-ebba02584ba5'::uuid,
    '5631000',
    'STRIA TTOyTTE MCPAL SABANETA',
    '05',
    '05631',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd144971e-6209-5931-b025-d0625b5a4969'::uuid,
    '5656001',
    'TRÁNSITO DPTAL DE ANTIOQUIA/SAN JERONIMO',
    '05',
    '05656',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '9922e675-9c95-52d7-be76-e4a7a05f5708'::uuid,
    '5686000',
    'STRIA TTO MCPAL SANTA ROSA DE OSOS',
    '05',
    '05686',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '97016ea8-be23-5ffa-a9b6-e856a2b8a258'::uuid,
    '5697000',
    'STRIA TTOyTTE EL SANTUARIO',
    '05',
    '05697',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '832bd9ac-50ae-5b79-8c18-34e295b8c604'::uuid,
    '5736000',
    'STRIA TTOyTTE MCPAL SEGOVIA',
    '05',
    '05736',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '4c25178a-e878-5a11-a632-7d45190e2681'::uuid,
    '5756000',
    'INSP TTOYTTE MCPAL SONSON',
    '05',
    '05756',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '328915f0-0bed-59b7-b1d7-5db8e9111284'::uuid,
    '5837000',
    'INSP MCPAL TTO TURBO',
    '05',
    '05837',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '46d49071-4218-5da8-bec1-6ef9c2825e4e'::uuid,
    '5847000',
    'STRIA TTOyTTE MCPAL URRAO',
    '05',
    '05847',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f1e6e518-f59b-592f-8d60-1b9a814a2647'::uuid,
    '5858000',
    'ALCALDIA MPAL DE VEGACHI',
    '05',
    '05858',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '63786320-2654-56ea-acd7-e3ae67497189'::uuid,
    '5887000',
    'SECRETARIA DE MOVILIDAD DE YARUMAL',
    '05',
    '05887',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '445f873e-a522-5810-b291-e14d1d0e2fa5'::uuid,
    '63001000',
    'STRIA DE TTOYTTE MCPAL ARMENIA',
    '63',
    '63001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'ddf6fd45-c662-5bd4-9f88-179216cc852b'::uuid,
    '63130000',
    'INSP TTOyTTE CALARCA',
    '63',
    '63130',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '098273a5-fd37-5489-8292-b2681c40c007'::uuid,
    '63190000',
    'INST DPTAL DE TTO QUINDIO/CIRCASIA',
    '63',
    '63190',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'e9380ec4-f19d-501f-bea2-823244ea5e31'::uuid,
    '63401000',
    'STRIA TTOyTTE MCPAL LA TEBAIDA',
    '63',
    '63401',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '3ee4c34e-903a-54e1-9319-5c6759710f53'::uuid,
    '63594000',
    'INS DE TTOyTTE QUIMBAYA',
    '63',
    '63594',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '9e038db6-8144-5da9-b543-dc58e5806db1'::uuid,
    '66001000',
    'INSTITUTO DE MOVILIDAD DE PEREIRA',
    '66',
    '66001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '63052d0e-31a1-54f7-b281-512bc3666edd'::uuid,
    '66170000',
    'STRIA MCPAL TTOyTTE DOSQUEBRADAS',
    '66',
    '66170',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'b4336c8e-dc9e-5105-a43c-167a67b5dc4f'::uuid,
    '66400000',
    'STRIA MCPAL TTOyTTE LA VIRGINIA',
    '66',
    '66400',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '9deb795c-0b33-5b0d-8e1a-d9ef32516c29'::uuid,
    '66682000',
    'STRIA TTO y GOB MCPAL SANTA ROSA CABAL',
    '66',
    '66682',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'aaaaaaaa-0001-4000-8000-000000000005'::uuid,
    '68001000',
    'DIR TTOyTTE BUCARAMANGA',
    '68',
    '68001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f76c325e-187e-574e-931c-77b1cd8d45fd'::uuid,
    '68077000',
    'INSP MCPAL TTOyTTE BARBOSA/STDER',
    '68',
    '68077',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '879c2dbe-f8fa-5a8a-a121-48ada05267f6'::uuid,
    '68079000',
    'STRIA TTOyTTE MCPAL BARICHARA/SANTANDER',
    '68',
    '68079',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '08c227fc-aeb9-5db1-b086-e4a2ed323000'::uuid,
    '68081000',
    'INSP TTOyTTE BARRANCABERMEJA',
    '68',
    '68081',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '55b9de34-c6f6-58da-a710-554991bf0369'::uuid,
    '68167000',
    'INST TTOyTTE CHARALA',
    '68',
    '68167',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '5524cbc1-548a-554f-b092-aec94b722809'::uuid,
    '68190000',
    'STRIA TTOyTTE MCPAL CIMITARRA',
    '68',
    '68190',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'b590c670-cb76-52ea-9a71-d3d4e0c10ea4'::uuid,
    '68276000',
    'DIR TTOyTTE FLORIDABLANCA',
    '68',
    '68276',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '6937c17f-2ed4-5bec-8262-14d3d28c829d'::uuid,
    '68307000',
    'STRIA MCPAL TTOyTTE GIRON',
    '68',
    '68307',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '0612c33d-a2c0-5c3e-bc2b-054698f48882'::uuid,
    '68406000',
    'STARIA TTO Y MOVILIDAD DEL MCPIO DE LEBRIJA',
    '68',
    '68406',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '0e745b34-a58e-5c00-8e54-3a3908492294'::uuid,
    '68432000',
    'STRIA TTOyTTE MCPAL MALAGA',
    '68',
    '68432',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'fd557bfd-fc5a-58d0-a690-558a56cb8f77'::uuid,
    '68500000',
    'STRIA TTOyTTE MCPAL DE OIBA',
    '68',
    '68500',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '0943a1cc-fd13-548e-a89a-c8f27c8702ac'::uuid,
    '68547000',
    'STRIA TTOyMOV PIEDECUESTA/SANTANDER',
    '68',
    '68547',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '3feb4293-d9b6-53d6-b59a-16921007a9ad'::uuid,
    '68572000',
    'STRIA TTOyTTE MCPAL PUENTE NACIONAL/SANTANDER',
    '68',
    '68572',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a540b12c-a48f-584b-ba14-70c1cc1a43a4'::uuid,
    '68655000',
    'STRIA TTOyTTE MCPAL SABANA DE TORRES',
    '68',
    '68655',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '00612b4c-4225-519d-a3d2-0b9467151f2e'::uuid,
    '68679000',
    'INSP MCPAL TTOyTTE SAN GIL',
    '68',
    '68679',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f8f69758-788f-5723-b2bd-b8ecaf9febcd'::uuid,
    '68689000',
    'INS MCPAL TTOyTTE SAN VICENTE CHUCURI',
    '68',
    '68689',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '140d2ed3-29b6-5fc8-9139-dda5b15956f7'::uuid,
    '68755000',
    'STRIA TTOyTTE MCPAL EL SOCORRO',
    '68',
    '68755',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '41040ba9-2abe-59ba-9423-af308b55ed6e'::uuid,
    '68861000',
    'STRIA TTOyTTE VELEZ',
    '68',
    '68861',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'cff7869a-a7f2-57f8-80ab-0bfaa6f594a5'::uuid,
    '70001000',
    'STRIA MCPAL TTEyTTO SINCELEJO',
    '70',
    '70001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'edf41c5b-f97d-5e8a-bf57-2a5ca22616fd'::uuid,
    '70215000',
    'INS MCPAL DE TyTO DE COROZAL',
    '70',
    '70215',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '08a3a8be-5545-5b64-a4ca-fb297596d3cc'::uuid,
    '70670000',
    'SECRETARIA DPTAL TTOyTTE SUCRE/SAMPUES',
    '70',
    '70670',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '5f9e305a-9b09-56a1-a9eb-7fb7f37afefb'::uuid,
    '70713000',
    'STRIA DE TTOyTTE MCPAL SAN ONOFRE',
    '70',
    '70713',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'c07fe916-4de0-5f60-bebb-dd21bb636cbd'::uuid,
    '70742000',
    'INST TTO Y TTE DEL MUNICIPIO DE SINCÉ',
    '70',
    '70742',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '625a949c-b0ac-5f67-85d0-cd75a4d7d4c6'::uuid,
    '73001000',
    'STRIA MCPAL TTOyTTE IBAGUE',
    '73',
    '73001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '502022a2-9a4c-5728-b717-c2512bd48200'::uuid,
    '73026000',
    'DPTO ADTVO TTOyTTE TOLIMA/ ALVARADO',
    '73',
    '73026',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '79b3a1a9-61fb-5f45-b75f-9b10ed4bb798'::uuid,
    '73055000',
    'DPTO ADTVO TTOyTTE TOLIMA/GUAYABAL',
    '73',
    '73055',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '29a74d7f-949e-5f0a-8084-b35f98986282'::uuid,
    '73168000',
    'INST DE TTO MCPAL CHAPARRAL',
    '73',
    '73168',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a28bef72-d020-571a-9bf1-2f6b48c0ce3c'::uuid,
    '73268000',
    'STRIA MCPAL TTOyTTE ESPINAL',
    '73',
    '73268',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd3b18a23-e4a2-5d5c-9af8-da29d980af51'::uuid,
    '73283000',
    'STRIA TTOyTTE MCPAL FRESNO',
    '73',
    '73283',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '83a7b12f-68fe-54f3-acb9-689e0c0ab697'::uuid,
    '73319000',
    'DPTO ADTVO TTOyTTE TOLIMA/GUAMO',
    '73',
    '73319',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '15876029-0a9b-5af0-835e-54878c76d653'::uuid,
    '73349000',
    'STRIA MCPAL TTOyTTE HONDA',
    '73',
    '73349',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '252eca71-b378-5a9e-8ce9-1822bd5221ef'::uuid,
    '73411000',
    'STRIA TTOyTTE MCPAL LIBANO',
    '73',
    '73411',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '77ebf295-d212-5072-85bf-d0fcdf005c3b'::uuid,
    '73443000',
    'STRIA DE TTOyTTE MCPAL DE SAN SEBASTIAN DE MARIQUITA TOLIMA',
    '73',
    '73443',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'ac2f72ba-6e52-5422-a69e-3b5a23796527'::uuid,
    '73449000',
    'STRIA GOB TTOyTTE MELGAR',
    '73',
    '73449',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'cf5bf8e6-fa98-5654-b354-6c9948f27691'::uuid,
    '73504000',
    'DPTO ADTVO TTOyTTE TOLIMA/ORTEGA',
    '73',
    '73504',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '943f525b-69aa-5258-b330-24beed7ecddb'::uuid,
    '73585000',
    'DPTO ADTVO TTOyTTE TOLIMA/PURIFICACIÓN',
    '73',
    '73585',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'aaaaaaaa-0001-4000-8000-000000000003'::uuid,
    '76001000',
    'STRIA MCPAL TTO CALI',
    '76',
    '76001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a52879d3-8e57-5456-91ca-35957af33a4c'::uuid,
    '76020000',
    'STRIA MOVyTTE DPTAL VALLE DEL CAUCA/ALCALA',
    '76',
    '76020',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a7fede7e-d92f-5f4c-b4b8-95e868034514'::uuid,
    '76036000',
    'STRIA TTOyTTE ANDALUCIA',
    '76',
    '76036',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '251b2b5d-35cd-527a-ae62-a9ddfc71fc79'::uuid,
    '76041000',
    'STRIA MOVyTTE DPTAL VALLE DEL CAUCA/ANSERMANUEVO',
    '76',
    '76041',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'c0705318-06e4-56e5-8726-15a365c01d57'::uuid,
    '76100000',
    'STRIA MOVyTTE DPTAL VALLE DEL CAUCA/BOLIVAR',
    '76',
    '76100',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f871be49-017b-5529-81f1-1ac5c4efb446'::uuid,
    '76109000',
    'STRIA TTOyTTE MCPAL BUENAVENTURA',
    '76',
    '76109',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '02cf6096-661b-5d81-9a1a-20da54ac160d'::uuid,
    '76111000',
    'INSP TTEyTTO GUADALAJARA DE BUGA',
    '76',
    '76111',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '7d92c333-adaf-530c-9869-ebca491cb8fe'::uuid,
    '76113000',
    'STRIA MOVyTTE DPTAL VALLE DEL CAUCA/BUGALAGRANDE',
    '76',
    '76113',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '02f07c3b-2175-5a49-87c8-9304f610a0e1'::uuid,
    '76122000',
    'STRIA MCPAL TTOyTTE CAICEDONIA',
    '76',
    '76122',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '26720643-6a63-59af-bfe4-66bf7e39bbdc'::uuid,
    '76130000',
    'STRIA TTOyTTE MCPAL CANDELARIA',
    '76',
    '76130',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f3b366c9-0027-5282-bb13-17a4605b0f16'::uuid,
    '76147000',
    'STRIA DE TTOyTTE CARTAGO',
    '76',
    '76147',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '69667d04-3c25-5a62-b900-aee7148893b7'::uuid,
    '76233000',
    'STRIA MOVyTTE DPTAL VALLE DEL CAUCA/DAGUA',
    '76',
    '76233',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '157b9828-8f0b-57be-8ff8-786c27cab591'::uuid,
    '76248000',
    'STRIA MCPAL TTOyTTE EL CERRITO',
    '76',
    '76248',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'ec8e449f-52b0-5a0b-959e-306af3366873'::uuid,
    '76275000',
    'STRIA TTOyTTE FLORIDA',
    '76',
    '76275',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '8f93fe66-e328-5336-a180-e0ce9cf35ebe'::uuid,
    '76306000',
    'STRIA DE TTOyTTE MCPAL DE GINEBRA',
    '76',
    '76306',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '00e6f21d-86ad-5cb9-a113-f0ba6dab56df'::uuid,
    '76318000',
    'STRIA TTOyTTE MCPAL GUACARI',
    '76',
    '76318',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd6beae9d-1db7-518f-a274-963c1561c334'::uuid,
    '76364000',
    'STRIA TTOyTTE MCPAL JAMUNDI',
    '76',
    '76364',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '977e709c-ba1c-5666-be30-29d808093400'::uuid,
    '76377000',
    'DIR TTOyTTE LA CUMBRE',
    '76',
    '76377',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '73dcc54c-c472-5278-bdea-b24772f577a3'::uuid,
    '76400000',
    'STRIA TTO MCPAL LA UNION',
    '76',
    '76400',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'c638ee27-2be1-59ea-b3a8-7d848215a835'::uuid,
    '76520000',
    'STRIA TTOyTTE PALMIRA',
    '76',
    '76520',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '35afd417-25bd-5df2-8fc7-b9bb7bb3efec'::uuid,
    '76563000',
    'STRIA TTOyTTE MCPAL PRADERA',
    '76',
    '76563',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '991cb838-fb72-51f2-a4de-241ef2425f6e'::uuid,
    '76622000',
    'INSP TTOyTTE ROLDANILLO',
    '76',
    '76622',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'bb780b62-a485-5422-876a-1e62a4dd29c5'::uuid,
    '76736000',
    'STRIA TTOyTTE SEVILLA',
    '76',
    '76736',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'a5562503-23ce-53f0-9ef8-2b10e22724cb'::uuid,
    '76834000',
    'STRIA TTO MCPAL TULUA',
    '76',
    '76834',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'f82d73a1-63cf-5313-98f3-f02fadf628bd'::uuid,
    '76869000',
    'SECRETARIA DE MOVILIDAD MUNICIPAL DE VIJES',
    '76',
    '76869',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '856af541-49b6-5b8a-b4bc-ea5a4fac44a8'::uuid,
    '76890000',
    'MUNICIPIO DE YOTOCO',
    '76',
    '76890',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '373c7514-a0ad-5704-a321-8d71fb1c4c86'::uuid,
    '76892000',
    'STRIA TTOyTTE YUMBO',
    '76',
    '76892',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'fa9f094d-a8fb-52d1-9962-8abd7dea8ae5'::uuid,
    '76895000',
    'INSP TTOyTTE ZARZAL',
    '76',
    '76895',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'aaaaaaaa-0001-4000-8000-000000000004'::uuid,
    '8001000',
    'STRIA DTAL TTO BARRANQUILLA',
    '08',
    '08001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '38487a59-3207-5d57-b448-5524b1c094ab'::uuid,
    '8078001',
    'INSTITUTO DE TRÁNSITO DEL ATLANTICO/BARANOA',
    '08',
    '08078',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '0f67510c-8040-5dab-965c-3af090886bda'::uuid,
    '81001000',
    'INST TTOyTTE ARAUCA/ARAUCA',
    '81',
    '81001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '65375663-6f46-524d-ab70-09faf7a170e3'::uuid,
    '81001001',
    'INST TTOyTTE  ARAUCA/ARAUCA',
    '81',
    '81001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '695a3686-cd4a-52f7-8075-8f69d0b04027'::uuid,
    '81065000',
    'INST MOVyTTE MCPAL DE ARAUQUITA',
    '81',
    '81065',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '0a9cb20b-25e7-5f85-9b82-dfc1ed9188de'::uuid,
    '81736000',
    'STRIA TTOyTTE MCPAL SARAVENA',
    '81',
    '81736',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '639fefc3-73dc-59f1-85ee-100a60c64661'::uuid,
    '81794000',
    'INST MOVyTTE MCPAL TAME',
    '81',
    '81794',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '4f826d49-48b8-53e6-a94e-b9432919fe67'::uuid,
    '8296000',
    'STRIA MCPAL TTOyTTE GALAPA',
    '08',
    '08296',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '774b6b2c-63dd-5256-bbaf-f74e03fed5b1'::uuid,
    '8433000',
    'STRIA DE TTOyTTE MALAMBO',
    '08',
    '08433',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '13fb2ba9-ad11-565d-9222-da0b5dcbe6fc'::uuid,
    '85001000',
    'STRIA TTOyTTE MCPAL YOPAL',
    '85',
    '85001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '3a367d93-f8cc-5a96-879f-7364ce410f3c'::uuid,
    '85010000',
    'DIR DPTAL TTOyTTE CASANARE/AGUAZUL',
    '85',
    '85010',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '1d95d028-4761-5e2c-94b3-31c95bb6857c'::uuid,
    '85440000',
    'STRIA TTOyMOV MCPAL VILLANUEVA',
    '85',
    '85440',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '81ecdb8b-d704-5975-ac08-040800e669af'::uuid,
    '8573000',
    'STRIA MCPAL TTOyTTE PUERTO COLOMBIA',
    '08',
    '08573',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '35d301eb-6f3a-5ac9-8b44-3855dac9e72c'::uuid,
    '86001000',
    'STRIA TTOyTTE MOCOA',
    '86',
    '86001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '78b1f63f-2f4c-5938-9642-ed2cb3a3fe79'::uuid,
    '86320000',
    'STRIA TTOyTTE MCPAL ORITO',
    '86',
    '86320',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '705ce7db-7c74-555a-8a8f-a5a749fa257f'::uuid,
    '8634000',
    'STRIA MCPAL TTOyTTE SABANAGRANDE– ATLÁNTICO',
    '08',
    '08634',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'b2131e68-5bd6-5ced-9e15-ea414ff4b624'::uuid,
    '8638000',
    'STRIA TTO Y TTE MCPAL DE SABANALARGA',
    '08',
    '08638',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '861ccc16-8a9e-5d8c-b600-dfad062a7b0c'::uuid,
    '86568000',
    'INST MCPAL TTE Y MOVILIDAD-IMTRAM PTO ASIS',
    '86',
    '86568',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '70e72982-0f40-5cb3-92e4-6ed075e9ed8f'::uuid,
    '86749001',
    'TRANSITO DPTAL DE PUTUMAYO/ SIBUNDOY',
    '86',
    '86749',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '574170e6-96aa-599d-a32b-0a51b5442a9a'::uuid,
    '86865000',
    'SRIA TTOyTTE MPAL VALLE GUAMUEZ/HORMIGA',
    '86',
    '86865',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '9fc0d03e-416d-5a45-89c4-b3273d1c6945'::uuid,
    '86885000',
    'DEPTO ADMTVO DE TTO Y TTE PUTUMAYO',
    '86',
    '86885',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '246b9b68-57df-5136-a328-b642bf6d9c25'::uuid,
    '8758000',
    'INST MCPAL TTOYTTE SOLEDAD',
    '08',
    '08758',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '1d1a4f7e-b807-544a-b925-b3edf319bf56'::uuid,
    '88001000',
    'DIR DPTAL TTOyTTE SAN ANDRES ISLAS',
    '88',
    '88001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '14b4c7d6-12c6-55ae-acb9-c1f57ad210cb'::uuid,
    '91001000',
    'INSP MCPAL TTOyREG VIAL LETICIA',
    '91',
    '91001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '6463c4f0-27fd-543b-ac31-e420dfdd7585'::uuid,
    '94001000',
    'STRIA TTOyTTE MCPAL INIRIDA',
    '94',
    '94001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    '9d1eb955-39e1-5baf-8961-3cda57f25075'::uuid,
    '95001000',
    'STRIA TTOyTTE MCPAL SAN JOSE GUAVIARE',
    '95',
    '95001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code, is_active)
VALUES (
    'd806ad56-63c6-56da-ba09-9153a1c2ade8'::uuid,
    '99001000',
    'F. DPTAL TTO VICHADA/PUERTO CARREÑO',
    '99',
    '99001',
    true
)
ON CONFLICT (code) DO UPDATE SET
    name = EXCLUDED.name,
    department_code = EXCLUDED.department_code,
    city_code = EXCLUDED.city_code,
    is_active = EXCLUDED.is_active;

-- Grant opcional dev: Funza habilitado para FLITDEV (pruebas B11 auto-bind).
INSERT INTO admin.tenant_transit_office_grants (id, tenant_id, transit_office_id, is_enabled, created_at)
SELECT uuidv7(), '11111111-1111-1111-1111-111111111111'::uuid, id, true, now()
FROM catalogs.transit_offices
WHERE code = '25286000'
ON CONFLICT (tenant_id, transit_office_id) DO NOTHING;

COMMIT;
