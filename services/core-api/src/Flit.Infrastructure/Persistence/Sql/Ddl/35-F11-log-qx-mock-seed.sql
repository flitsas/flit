-- Feature #10792 (LOG QX) — datos mock de trazabilidad Quipux para revisión en DEV/QA.
--
-- DEV-ONLY: lo invoca la migración F11_LogQxMockSeed SOLO cuando IsDevSeedEnabled() (Development/QA
-- o FLIT_DEV_SEED), mismo gate que 12-HU10200-dev-seed / FEATURE10707_AvaluoMockSeed. NO es dato de
-- negocio: son 5 radicaciones de ejemplo (una por cada estado) para poder revisar el módulo LOG QX
-- (?m=log-qx) sin depender de la integración real.
--
-- El LOG QX hace INNER JOIN submissions × procedure_instances × procedure_types × tenants, así que
-- los 5 trámites QXSEED deben existir o no se ve nada. Sus FK padres ya vienen de seeds dev
-- comiteados con el MISMO gate: tenant 0ad1c0de-…-0008 (SeedMockCompanies), tipos 33333333/44444444
-- (SeedProcedureTypes), usuario 22222222 (HU10200_DevSeed — 11111111 es el TENANT FLITDEV, no un user),
-- OT aaaaaaaa-0001 (HU10133_OtAdminDevSeed / catálogo RUNT).
--
-- Sin BEGIN/COMMIT: EF envuelve la migración en su propia transacción. Idempotente y determinista:
-- ON CONFLICT en las instancias; delete-then-insert en placas / radicaciones / eventos (re-ejecutable
-- y sin duplicar si el ambiente ya tenía el mock de una siembra manual previa).

-- RLS off en la transacción de la migración: corre sin app.current_tenant_id (igual que
-- 33-HU10710-quipux-external-refs-seed.sql). El rol de la migración es dueño de las tablas.
SET LOCAL row_security = off;

-- ============================================================================
-- 1. Trámites QXSEED (5) — uno por familia. ON CONFLICT: no se pisan si ya existen.
--    WHERE EXISTS: si el seed del usuario/tenant no corrió, se omite en vez de fallar la migración.
-- ============================================================================
INSERT INTO tramites.procedure_instances
    (id, tenant_id, procedure_type_id, reference_number, created_by_user_id,
     transit_office_id, status, modalidad_entrada, checklist_estado)
SELECT
    v.id, '0ad1c0de-0000-4000-8000-000000000008'::uuid, pt.id, v.reference_number,
    '22222222-2222-2222-2222-222222222222'::uuid, 'aaaaaaaa-0001-4000-8000-000000000001'::uuid,
    'entregado', v.modalidad, '{}'::jsonb
FROM (VALUES
    ('5eed0001-0000-4000-8000-000000000001'::uuid, 'TRASPASO_STANDARD', 'QXSEED-001', 'traspaso'),
    ('5eed0001-0000-4000-8000-000000000002'::uuid, 'MATRICULA_NUEVA', 'QXSEED-002', 'matricula_inicial'),
    ('5eed0001-0000-4000-8000-000000000003'::uuid, 'TRASPASO_STANDARD', 'QXSEED-003', 'traspaso'),
    ('5eed0001-0000-4000-8000-000000000004'::uuid, 'MATRICULA_NUEVA', 'QXSEED-004', 'matricula_inicial'),
    ('5eed0001-0000-4000-8000-000000000005'::uuid, 'TRASPASO_STANDARD', 'QXSEED-005', 'traspaso')
) AS v(id, procedure_type_code, reference_number, modalidad)
JOIN tramites.procedure_types pt ON pt.code = v.procedure_type_code
WHERE EXISTS (SELECT 1 FROM identity.users u   WHERE u.id = '22222222-2222-2222-2222-222222222222')
  AND EXISTS (SELECT 1 FROM identity.tenants t WHERE t.id = '0ad1c0de-0000-4000-8000-000000000008')
  AND EXISTS (SELECT 1 FROM catalogs.transit_offices o WHERE o.id = 'aaaaaaaa-0001-4000-8000-000000000001')
ON CONFLICT (tenant_id, reference_number) DO NOTHING;

-- ============================================================================
-- 2. Placas (field_values) — habilitan la búsqueda por placa.
--    Los trámites están 'entregado', así que el trigger de inmutabilidad bloquea el INSERT: se
--    desactiva SOLO durante el seed y se reactiva enseguida (los datos siguen siendo válidos).
-- ============================================================================
ALTER TABLE tramites.procedure_instance_field_values
  DISABLE TRIGGER tr_procedure_instance_field_values_immutable;

DELETE FROM tramites.procedure_instance_field_values
 WHERE field_key = 'plate'
   AND procedure_instance_id IN (
     '5eed0001-0000-4000-8000-000000000001','5eed0001-0000-4000-8000-000000000002',
     '5eed0001-0000-4000-8000-000000000003','5eed0001-0000-4000-8000-000000000004',
     '5eed0001-0000-4000-8000-000000000005');

INSERT INTO tramites.procedure_instance_field_values
    (id, tenant_id, procedure_instance_id, field_key, value_text, source)
SELECT v.id, '0ad1c0de-0000-4000-8000-000000000008'::uuid, v.instance, 'plate', v.plate, 'user'
FROM (VALUES
    ('5eedf1e1-0000-4000-8000-000000000001'::uuid, '5eed0001-0000-4000-8000-000000000001'::uuid, 'ABC123'),
    ('5eedf1e1-0000-4000-8000-000000000002'::uuid, '5eed0001-0000-4000-8000-000000000002'::uuid, 'DEF456'),
    ('5eedf1e1-0000-4000-8000-000000000003'::uuid, '5eed0001-0000-4000-8000-000000000003'::uuid, 'GHI789'),
    ('5eedf1e1-0000-4000-8000-000000000004'::uuid, '5eed0001-0000-4000-8000-000000000004'::uuid, 'JKL012'),
    ('5eedf1e1-0000-4000-8000-000000000005'::uuid, '5eed0001-0000-4000-8000-000000000005'::uuid, 'MNO345')
) AS v(id, instance, plate)
WHERE EXISTS (SELECT 1 FROM tramites.procedure_instances pi WHERE pi.id = v.instance);

ALTER TABLE tramites.procedure_instance_field_values
  ENABLE TRIGGER tr_procedure_instance_field_values_immutable;

-- ============================================================================
-- 3. Radicaciones (quipux_submissions) — delete-then-insert (el DELETE cascada limpia los eventos).
--    Códigos QX reales: qx_register_code = codigo de "Registro documento" (81 = almacenado OK);
--    qx_procedure_code = estadoTramite.codigo del sondeo (2 = aprobado, 3 = rechazado).
-- ============================================================================
DELETE FROM tramites.quipux_submissions
 WHERE id IN (
   '5eed5b01-0000-4000-8000-000000000001','5eed5b01-0000-4000-8000-000000000002',
   '5eed5b01-0000-4000-8000-000000000003','5eed5b01-0000-4000-8000-000000000004',
   '5eed5b01-0000-4000-8000-000000000005');

INSERT INTO tramites.quipux_submissions
    (id, tenant_id, procedure_instance_id, document_name, attachment_id,
     quipux_procedure_type, quipux_requirement_type, divipo_code, status,
     qx_register_code, qx_procedure_code, rejection_reason, attempts, poll_count)
SELECT
    v.id, '0ad1c0de-0000-4000-8000-000000000008'::uuid, v.instance, v.doc, v.attachment,
    v.qtype, 1, '11001000', v.status, v.reg, v.proc, v.rejection, v.attempts, v.polls
FROM (VALUES
    ('5eed5b01-0000-4000-8000-000000000001'::uuid, '5eed0001-0000-4000-8000-000000000001'::uuid, 'FLIT_QXSEED-001', '5eeda77a-0000-4000-8000-000000000001'::uuid, 16, 'pendiente',  NULL::int, NULL::int, NULL::varchar,                                    0, 0),
    ('5eed5b01-0000-4000-8000-000000000002'::uuid, '5eed0001-0000-4000-8000-000000000002'::uuid, 'FLIT_QXSEED-002', '5eeda77a-0000-4000-8000-000000000002'::uuid, 13, 'registrado', 81,        NULL,      NULL,                                          1, 2),
    ('5eed5b01-0000-4000-8000-000000000003'::uuid, '5eed0001-0000-4000-8000-000000000003'::uuid, 'FLIT_QXSEED-003', '5eeda77a-0000-4000-8000-000000000003'::uuid, 16, 'aprobado',   81,        2,         NULL,                                          1, 3),
    ('5eed5b01-0000-4000-8000-000000000004'::uuid, '5eed0001-0000-4000-8000-000000000004'::uuid, 'FLIT_QXSEED-004', '5eeda77a-0000-4000-8000-000000000004'::uuid, 13, 'rechazado',  81,        3,         'Documento consolidado ilegible',              1, 4),
    ('5eed5b01-0000-4000-8000-000000000005'::uuid, '5eed0001-0000-4000-8000-000000000005'::uuid, 'FLIT_QXSEED-005', '5eeda77a-0000-4000-8000-000000000005'::uuid, 16, 'fallido',    NULL,      NULL,      'Radicación fallida: timeout (72) y luego error interno del OT (76) tras reintento; máx. intentos alcanzado', 5, 0)
) AS v(id, instance, doc, attachment, qtype, status, reg, proc, rejection, attempts, polls)
WHERE EXISTS (SELECT 1 FROM tramites.procedure_instances pi WHERE pi.id = v.instance);

-- ============================================================================
-- 4. Timeline (quipux_submission_events, 32) — un recorrido por cada estado, con duration_ms/origen/
--    codigo en el detail, errores y reintentos, enmascarado (propietario_*) y eventos sin payload
--    (detail NULL = "sin payload disponible"). id por defecto (uuidv7); el DELETE de arriba ya limpió.
--    WHERE EXISTS por submission: si la sección 3 omitió las radicaciones (padres ausentes), no
--    insertamos huérfanos — evita 23503 en CI cuando el seed se ejecuta a medias.
-- ============================================================================
INSERT INTO tramites.quipux_submission_events
    (tenant_id, submission_id, stage, outcome, detail, correlation_id, occurred_at)
SELECT v.tenant_id, v.submission_id, v.stage, v.outcome, v.detail, v.correlation_id, v.occurred_at
FROM (VALUES
  -- 001 · PENDIENTE (en cola)
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000001'::uuid,'claimed','ok','{"origen":"quipux_register","batch":"BATCH-001"}'::jsonb,'5eedc0aa-0000-4000-8000-000000000001'::uuid,'2026-07-20 08:00:00+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000001'::uuid,'consolidado_generado','ok','{"origen":"quipux_register","duration_ms":320,"paginas":8}'::jsonb,'5eedc0aa-0000-4000-8000-000000000001'::uuid,'2026-07-20 08:00:01+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000001'::uuid,'s3_subido','ok','{"origen":"quipux_register","duration_ms":540,"bucket":"qxinterconnect","key":"FLIT/QXSEED-001.pdf"}'::jsonb,'5eedc0aa-0000-4000-8000-000000000001'::uuid,'2026-07-20 08:00:02+00'::timestamptz),
  -- 002 · REGISTRADO (81 = almacenado OK)
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000002'::uuid,'claimed','ok','{"origen":"quipux_register","batch":"BATCH-002"}'::jsonb,'5eedc0aa-0000-4000-8000-000000000002'::uuid,'2026-07-20 09:00:00+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000002'::uuid,'consolidado_generado','ok','{"origen":"quipux_register","duration_ms":410}'::jsonb,'5eedc0aa-0000-4000-8000-000000000002'::uuid,'2026-07-20 09:00:01+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000002'::uuid,'s3_subido','ok','{"origen":"quipux_register","duration_ms":600}'::jsonb,'5eedc0aa-0000-4000-8000-000000000002'::uuid,'2026-07-20 09:00:02+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000002'::uuid,'registro_enviado','ok','{"origen":"quipux_register","placa":"DEF456","vin":"","documento":"FLIT_QXSEED-002","tipo_tramite":13,"tipo_requisito":1,"codigo_divipo":"11001000","intento":1}'::jsonb,'5eedc0aa-0000-4000-8000-000000000002'::uuid,'2026-07-20 09:00:03+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000002'::uuid,'registro_respuesta','ok','{"origen":"quipux_register","duration_ms":1240,"codigo":81,"mensaje":"Los datos se almacenaron correctamente"}'::jsonb,'5eedc0aa-0000-4000-8000-000000000002'::uuid,'2026-07-20 09:00:05+00'::timestamptz),
  -- 003 · APROBADO (estadoTramite 2; el evento aprobado lleva PII → enmascarado por el backend)
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000003'::uuid,'claimed','ok','{"origen":"quipux_register","batch":"BATCH-003"}'::jsonb,'5eedc0aa-0000-4000-8000-000000000003'::uuid,'2026-07-20 10:00:00+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000003'::uuid,'consolidado_generado','ok','{"origen":"quipux_register","duration_ms":300}'::jsonb,'5eedc0aa-0000-4000-8000-000000000003'::uuid,'2026-07-20 10:00:01+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000003'::uuid,'s3_subido','ok','{"origen":"quipux_register","duration_ms":520}'::jsonb,'5eedc0aa-0000-4000-8000-000000000003'::uuid,'2026-07-20 10:00:02+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000003'::uuid,'registro_enviado','ok','{"origen":"quipux_register","placa":"GHI789","vin":"","documento":"FLIT_QXSEED-003","tipo_tramite":16,"tipo_requisito":1,"codigo_divipo":"11001000","intento":1}'::jsonb,'5eedc0aa-0000-4000-8000-000000000003'::uuid,'2026-07-20 10:00:03+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000003'::uuid,'registro_respuesta','ok','{"origen":"quipux_register","duration_ms":1100,"codigo":81,"mensaje":"Los datos se almacenaron correctamente"}'::jsonb,'5eedc0aa-0000-4000-8000-000000000003'::uuid,'2026-07-20 10:00:05+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000003'::uuid,'consulta_enviada','ok','{"origen":"quipux_status_poll"}'::jsonb,'5eedc0aa-0000-4000-8000-000000000003'::uuid,'2026-07-20 11:00:00+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000003'::uuid,'consulta_respuesta','ok','{"origen":"quipux_status_poll","duration_ms":430,"codigo":81,"estado_tramite":1,"sigue_en_tramite":true}'::jsonb,'5eedc0aa-0000-4000-8000-000000000003'::uuid,'2026-07-20 11:00:01+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000003'::uuid,'consulta_respuesta','ok','{"origen":"quipux_status_poll","duration_ms":390,"codigo":81,"estado_tramite":2}'::jsonb,'5eedc0aa-0000-4000-8000-000000000003'::uuid,'2026-07-20 12:00:00+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000003'::uuid,'aprobado','ok','{"origen":"quipux_status_poll","codigo":81,"estado_tramite":2,"propietario_documento":"1094567890","propietario_nombre":"Juan Perez Gomez"}'::jsonb,'5eedc0aa-0000-4000-8000-000000000003'::uuid,'2026-07-20 12:00:01+00'::timestamptz),
  -- 004 · RECHAZADO (estadoTramite 3; incluye un evento con detail NULL = "sin payload disponible")
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000004'::uuid,'claimed','ok','{"origen":"quipux_register","batch":"BATCH-004"}'::jsonb,'5eedc0aa-0000-4000-8000-000000000004'::uuid,'2026-07-20 13:00:00+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000004'::uuid,'s3_subido','ok',NULL::jsonb,'5eedc0aa-0000-4000-8000-000000000004'::uuid,'2026-07-20 13:00:02+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000004'::uuid,'registro_enviado','ok','{"origen":"quipux_register","placa":"JKL012","vin":"","documento":"FLIT_QXSEED-004","tipo_tramite":13,"tipo_requisito":1,"codigo_divipo":"11001000","intento":1}'::jsonb,'5eedc0aa-0000-4000-8000-000000000004'::uuid,'2026-07-20 13:00:03+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000004'::uuid,'registro_respuesta','ok','{"origen":"quipux_register","duration_ms":980,"codigo":81,"mensaje":"Los datos se almacenaron correctamente"}'::jsonb,'5eedc0aa-0000-4000-8000-000000000004'::uuid,'2026-07-20 13:00:05+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000004'::uuid,'consulta_respuesta','ok','{"origen":"quipux_status_poll","duration_ms":410,"codigo":81,"estado_tramite":3}'::jsonb,'5eedc0aa-0000-4000-8000-000000000004'::uuid,'2026-07-20 14:00:00+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000004'::uuid,'rechazado','error_definitivo','{"origen":"quipux_status_poll","estado_tramite":3,"motivo":"Documento consolidado ilegible"}'::jsonb,'5eedc0aa-0000-4000-8000-000000000004'::uuid,'2026-07-20 14:00:01+00'::timestamptz),
  -- 005 · FALLIDO (errores + reintento manual; incluye un evento con detail NULL)
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000005'::uuid,'claimed','ok','{"origen":"quipux_register","batch":"BATCH-005"}'::jsonb,'5eedc0aa-0000-4000-8000-000000000005'::uuid,'2026-07-20 15:00:00+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000005'::uuid,'consolidado_generado','ok',NULL::jsonb,'5eedc0aa-0000-4000-8000-000000000005'::uuid,'2026-07-20 15:00:01+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000005'::uuid,'s3_subido','ok','{"origen":"quipux_register","duration_ms":700}'::jsonb,'5eedc0aa-0000-4000-8000-000000000005'::uuid,'2026-07-20 15:00:02+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000005'::uuid,'registro_enviado','ok','{"origen":"quipux_register","placa":"MNO345","vin":"","documento":"FLIT_QXSEED-005","tipo_tramite":16,"tipo_requisito":1,"codigo_divipo":"11001000","intento":1}'::jsonb,'5eedc0aa-0000-4000-8000-000000000005'::uuid,'2026-07-20 15:00:03+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000005'::uuid,'registro_error','error_transitorio','{"origen":"quipux_register","duration_ms":60000,"codigo":72,"mensaje":"Se ha excedido el tiempo máximo de espera","intento":1}'::jsonb,'5eedc0aa-0000-4000-8000-000000000005'::uuid,'2026-07-20 15:01:03+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000005'::uuid,'reintento_manual','ok','{"origen":"manual","motivo":"Re-encolado por soporte"}'::jsonb,'5eedc0bb-0000-4000-8000-000000000005'::uuid,'2026-07-20 16:00:00+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000005'::uuid,'registro_enviado','ok','{"origen":"quipux_register","placa":"MNO345","vin":"","documento":"FLIT_QXSEED-005","tipo_tramite":16,"tipo_requisito":1,"codigo_divipo":"11001000","intento":2}'::jsonb,'5eedc0bb-0000-4000-8000-000000000005'::uuid,'2026-07-20 16:00:03+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000005'::uuid,'registro_error','error_definitivo','{"origen":"quipux_register","duration_ms":850,"codigo":76,"mensaje":"Información no disponible (error interno del organismo de tránsito)","intento":2}'::jsonb,'5eedc0bb-0000-4000-8000-000000000005'::uuid,'2026-07-20 16:00:05+00'::timestamptz),
  ('0ad1c0de-0000-4000-8000-000000000008'::uuid,'5eed5b01-0000-4000-8000-000000000005'::uuid,'dead_letter','error_definitivo','{"origen":"quipux_register","intentos":2,"motivo":"max_attempts alcanzado"}'::jsonb,'5eedc0bb-0000-4000-8000-000000000005'::uuid,'2026-07-20 16:00:06+00'::timestamptz)
) AS v(tenant_id, submission_id, stage, outcome, detail, correlation_id, occurred_at)
WHERE EXISTS (SELECT 1 FROM tramites.quipux_submissions s WHERE s.id = v.submission_id);
