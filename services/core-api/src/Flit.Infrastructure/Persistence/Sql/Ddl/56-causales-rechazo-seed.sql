-- Siembra del catálogo de causales de rechazo, portado desde FLIT 1 (rejection_type_master).
--
-- Origen: 19 filas activas en la base de FLIT 1 — 9 de matrícula (process_type='registration') y
-- 10 de traspaso (process_type='transfer'). Se siembran 18: «NO TIENE IMPRONTAS» estaba DUPLICADO
-- (ids 34 y 35) y se unifica en una sola causal.
--
-- Los textos se normalizan a mayúscula inicial y se les quita el prefijo «MATRICULA-» (redundante:
-- la columna `modalidad` ya lo dice). El contenido no cambia.
--
-- Idempotente por `code`: re-ejecutar no duplica ni pisa lo que SuperAdmin haya editado después.
-- Por eso NO hay ON CONFLICT DO UPDATE — la siembra pone el punto de partida, no lo impone.

INSERT INTO catalogs.rejection_reasons (code, description, modalidad, sort_order)
VALUES
    -- ── Matrícula inicial ───────────────────────────────────────────────────────
    ('improntas_no_adjuntas',           'Improntas no se adjuntaron',                     'matricula_inicial', 10),
    ('improntas_borrosas',              'Improntas están borrosas',                       'matricula_inicial', 20),
    ('improntas_mal_cargadas',          'Improntas mal cargadas',                         'matricula_inicial', 30),
    ('improntas',                       'Improntas',                                      'matricula_inicial', 40),
    ('factura_electronica_compra',      'Factura electrónica de compra',                  'matricula_inicial', 50),
    ('certificado_emision_gases',       'Certificado de emisión de gases',                'matricula_inicial', 60),
    ('manifiesto_aduana',               'Manifiesto de aduana / declaración de importación', 'matricula_inicial', 70),
    ('no_pago_impuesto_departamental',  'No pago de impuesto departamental',              'matricula_inicial', 80),
    -- «Otros» va de último a propósito: es la vía de escape, y en el reporte su peso mide cuánto
    -- se le está escapando al catálogo.
    ('otros_matricula',                 'Otros',                                          'matricula_inicial', 999),

    -- ── Traspaso ────────────────────────────────────────────────────────────────
    ('no_tiene_improntas',              'No tiene improntas',                             'traspaso', 10),
    ('soat_no_vigente',                 'SOAT no vigente',                                'traspaso', 20),
    ('impuestos_no_vigentes',           'Impuestos no vigentes',                          'traspaso', 30),
    ('no_tiene_documento_comprador',    'No tiene documento del comprador',               'traspaso', 40),
    ('inconsistencia_runt_vendedor',    'Inconsistencia inscripción en el RUNT del vendedor',  'traspaso', 50),
    ('inconsistencia_runt_comprador',   'Inconsistencia inscripción en el RUNT del comprador', 'traspaso', 60),
    ('escritura_no_vigente_vendedor',   'Escritura no vigente del vendedor',              'traspaso', 70),
    ('escritura_no_vigente_comprador',  'Escritura no vigente del comprador',             'traspaso', 80),
    ('otros_traspaso',                  'Otros',                                          'traspaso', 999)
ON CONFLICT (code) DO NOTHING;
