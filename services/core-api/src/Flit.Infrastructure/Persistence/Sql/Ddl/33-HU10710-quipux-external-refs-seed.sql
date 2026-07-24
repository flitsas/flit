-- Integración Quipux (QX) — parametrización del mapeo en datos, no en código.
--
-- Complementa a 32-HU10710-quipux-integracion.sql (que crea las tablas) sembrando el mapeo que
-- decide QUÉ se radica y CON QUÉ códigos. Los dos catálogos ya tenían la columna external_refs
-- jsonb: aquí solo se rellena la clave 'quipux' de cada uno.
--
-- Por qué en external_refs y no en tablas/enums/código: el requisito es que la integración sirva
-- para TODO tipo de trámite y que añadir uno nuevo sea un UPDATE, sin desplegar. En FLIT 1.0 los
-- códigos 16/213/13 y los prefijos TR/TRU/MI/MIL eran literales repartidos entre servicios
-- distintos (traspasos y matrículas eran ramas de código separadas que hacían lo mismo), así que
-- cada trámite nuevo era código nuevo.
--
-- El GATE de elegibilidad es la AUSENCIA de la clave 'quipux': un procedure_type sin ella no se
-- radica (QuipuxTipoTramiteMap.Parse devuelve null) y un transit_office sin codigoDivipo tampoco
-- (no hay a qué secretaría radicar). No hay lista blanca aparte que se pueda desincronizar. Por eso
-- aquí NO se siembran DUPLICADO_PLACA, CAMBIO_SERVICIO ni LEVANTAMIENTO_PRENDA: aún no van a QX.
--
-- Idempotencia: cada UPDATE lleva un `IS DISTINCT FROM` contra el valor objetivo, así que en el
-- segundo arranque no toca ninguna fila. No es cosmético: procedure_types tiene los triggers
-- tr_procedure_types_audit y tr_procedure_types_row_version, de modo que un UPDATE incondicional
-- escribiría una fila en audit.audit_logs y subiría row_version en CADA arranque
-- (Database:AutoMigrate corre esto siempre). Al ser condicional, un cambio manual del operador en
-- PDN tampoco se pisa en silencio... salvo que coincida exactamente con lo sembrado.
--
-- Ojo: el seed matchea por `code` a propósito, no por UUID. El catálogo procedure_types está
-- solapado por tres seeds históricos (migración SeedProcedureTypes con UUID fijos y family
-- VEHICULAR; 04-HU10151-seeds-minimos.sql con uuidv7() y familias MATRICULAS/TRASPASO/OTROS;
-- 15-tramites-traspaso-dev-seed.sql con TRASPASO_STANDARD), y qué códigos existen depende del
-- ambiente. Cubrir todos los códigos conocidos de traspaso y matrícula hace el seed correcto en
-- cualquiera de ellos; los que no existan simplemente no matchean.

-- Sin BEGIN/COMMIT propios: EF ya envuelve cada migración en una transacción, así que un COMMIT
-- aquí la cerraría a media migración y todo lo posterior fallaría con "Transaction is already
-- completed". Por eso los DDL de este directorio que se cargan con EmbeddedDdl.LoadUp (p. ej.
-- 31-HU10624) no los llevan. El SET LOCAL de abajo sigue siendo transaccional: vive en la
-- transacción de la migración.

-- catalogs.transit_offices y tramites.procedure_types son catálogos globales (sin RLS), pero se
-- desactiva RLS igual que 27-HU10659-transit-offices-runt-catalog-seed.sql: el seed corre al
-- arrancar, sin app.current_tenant_id, y así no depende de que estas tablas nunca lleguen a
-- tenerlo.
SET LOCAL row_security = off;

-- ============================================================================
-- 1. tramites.procedure_types.external_refs → bloque 'quipux'
-- ============================================================================
-- Forma del bloque (la parsea Flit.Modules.Quipux.Domain.Mapeo.QuipuxTipoTramiteMap):
--   familia            MATRICULA | TRASPASO | OTROS — qué bandera de la secretaría manda
--   tipoTramite        código de trámite Quipux
--   tipoRequisito      código de requisito (51 en todos los flujos conocidos de 1.0)
--   prefijo            prefijo del nombre del documento
--   campoPlaca         field value del que sale la placa, o null si el trámite no la usa
--   campoVin           field value del que sale el VIN, o null si el trámite no lo usa
--   maxLongitudEmpresa tope de la parte de empresa del nombre del documento
--   variante           delta condicionado a un field value booleano; lo ausente hereda del base
--
-- tipoTramite, tipoRequisito, prefijo y maxLongitudEmpresa son obligatorios: un bloque a medio
-- llenar se trata como NO elegible (mejor no radicar que radicar con tipoTramite = 0).

-- Traspaso — tipoTramite 16 bilateral / 213 unilateral, prefijo TR / TRU.
-- El identificador del vehículo es la PLACA (el vehículo ya está matriculado).
-- La variante es_unilateral cambia código Y prefijo.
UPDATE tramites.procedure_types pt
SET external_refs = pt.external_refs || jsonb_build_object(
    'quipux', jsonb_build_object(
        'familia',            'TRASPASO',
        'tipoTramite',        16,
        'tipoRequisito',      51,
        'prefijo',            'TR',
        'campoPlaca',         'plate',
        'campoVin',           NULL,
        'maxLongitudEmpresa', 35,
        'variante', jsonb_build_object(
            'campo', 'es_unilateral',
            'cuandoVerdadero', jsonb_build_object(
                'tipoTramite', 213,
                'prefijo',     'TRU'
            )
        )
    )
)
WHERE pt.code IN (
    'TRASPASO',           -- migración SeedProcedureTypes (33333333-…), family VEHICULAR
    'TRASPASO_STANDARD',  -- 15-tramites-traspaso-dev-seed.sql — tipología MVP del motor
    'TRASPASO_SIMPLE',    -- 04-HU10151-seeds-minimos.sql, family TRASPASO
    'TRASPASO_LEASING'    -- ídem. Es un traspaso: el leasing NO cambia el tipoTramite Quipux
  )
  AND pt.external_refs -> 'quipux' IS DISTINCT FROM jsonb_build_object(
        'familia',            'TRASPASO',
        'tipoTramite',        16,
        'tipoRequisito',      51,
        'prefijo',            'TR',
        'campoPlaca',         'plate',
        'campoVin',           NULL,
        'maxLongitudEmpresa', 35,
        'variante', jsonb_build_object(
            'campo', 'es_unilateral',
            'cuandoVerdadero', jsonb_build_object(
                'tipoTramite', 213,
                'prefijo',     'TRU'
            )
        )
      );

-- Matrícula — tipoTramite 13 SIEMPRE, prefijo MI / MIL.
-- Dos asimetrías reales frente al traspaso, verificadas contra FLIT 1.0:
--   a) el identificador es el VIN, no la placa: el vehículo aún no tiene placa;
--   b) maxLongitudEmpresa es 25, NO 35.
-- La variante es_leasing cambia SOLO el prefijo (MI → MIL); el tipoTramite sigue siendo 13, por eso
-- 'cuandoVerdadero' no trae tipoTramite: lo ausente hereda del bloque base.
UPDATE tramites.procedure_types pt
SET external_refs = pt.external_refs || jsonb_build_object(
    'quipux', jsonb_build_object(
        'familia',            'MATRICULA',
        'tipoTramite',        13,
        'tipoRequisito',      51,
        'prefijo',            'MI',
        'campoPlaca',         NULL,
        'campoVin',           'vin',
        'maxLongitudEmpresa', 25,
        'variante', jsonb_build_object(
            'campo', 'es_leasing',
            'cuandoVerdadero', jsonb_build_object(
                'prefijo', 'MIL'
            )
        )
    )
)
WHERE pt.code IN (
    'MATRICULA_INICIAL',       -- migración SeedProcedureTypes (44444444-…), family VEHICULAR
    'MATRICULA_NUEVA',         -- 04-HU10151-seeds-minimos.sql, family MATRICULAS
    'MATRICULA_REACTIVACION'   -- ídem
  )
  AND pt.external_refs -> 'quipux' IS DISTINCT FROM jsonb_build_object(
        'familia',            'MATRICULA',
        'tipoTramite',        13,
        'tipoRequisito',      51,
        'prefijo',            'MI',
        'campoPlaca',         NULL,
        'campoVin',           'vin',
        'maxLongitudEmpresa', 25,
        'variante', jsonb_build_object(
            'campo', 'es_leasing',
            'cuandoVerdadero', jsonb_build_object(
                'prefijo', 'MIL'
            )
        )
      );

-- ============================================================================
-- 2. catalogs.transit_offices — NO se siembra, se carga a mano
-- ============================================================================
-- La secretaria destino se parametriza en columnas propias de catalogs.transit_offices
-- (divipo_code + quipux_registration/_transfer/_other), no aqui: ver la migracion
-- HU10710_TransitOfficeQuipuxFlags.
--
-- Y NO se siembra ningun valor, a proposito. El divipo_code se carga A MANO, secretaria por
-- secretaria, porque no se tienen los de las 317 del catalogo: el dato real sale de
-- traffic_secretaries.code_divipo de FLIT 1.0. Sembrar un valor plausible (p. ej. city_code) seria
-- peor que no sembrar: el gate de elegibilidad lo daria por bueno y se radicarian tramites en la
-- secretaria EQUIVOCADA, que es un error caro y silencioso. Sin divipo_code la secretaria no es
-- elegible y queda visible como pendiente en la consola — el fallo seguro.
--
-- Se carga con, p. ej.:
--   UPDATE catalogs.transit_offices
--   SET divipo_code = '11001', quipux_transfer = true
--   WHERE code = '11001000';

-- ─────────────────────────────────────────────────────────────────────────────
-- Queries de verificación post-seed
-- ─────────────────────────────────────────────────────────────────────────────
-- Tipos elegibles (esperado: los de traspaso y matrícula que existan en el ambiente):
--   SELECT code, external_refs -> 'quipux' ->> 'tipoTramite' AS tipo,
--          external_refs -> 'quipux' ->> 'prefijo' AS prefijo
--   FROM tramites.procedure_types
--   WHERE external_refs ? 'quipux' ORDER BY code;
--
-- OT elegibles (esperado: 6 en DEV, 0 en PDN hasta que llegue el dump de code_divipo):
--   SELECT code, name, external_refs -> 'quipux' ->> 'codigoDivipo' AS divipo
--   FROM catalogs.transit_offices
--   WHERE external_refs ? 'quipux' ORDER BY code;
