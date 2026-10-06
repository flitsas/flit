-- ─────────────────────────────────────────────────────────────────────────────
-- HU #12774 (Feature #12773, Épica #12754) — Certificado de Cámara de Comercio POR ROL.
--
-- El certificado de Cámara de Comercio acredita QUIÉN representa a una sociedad: pertenece a la
-- familia de las escrituras, no a la del baúl de firmas. Se pide a cada actor persona jurídica del
-- trámite, y se carga en el paso de ese actor.
--
-- UN CÓDIGO POR ROL, y no uno solo, por la misma razón que `escritura_representante{_rol}` y
-- `certificado_identidad{_rol}`: el emparejamiento adjunto ↔ requisito es por tipo, así que con un
-- único código el certificado del vendedor dejaría "satisfecho" también al comprador. En un traspaso
-- con las dos partes jurídicas eso es exactamente el caso que la HU viene a cubrir.
--
-- ROLES REALES DEL MODELO: vendedor, comprador y locatario (`ParteRol` → entidades OWNER, BUYER,
-- LESSEE). NO existe un rol 'propietario': se deriva de la modalidad — en traspaso es el vendedor y
-- en matrícula inicial el comprador — así que queda cubierto sin un código propio.
--
-- POR QUÉ NO SE REUTILIZA EL CÓDIGO `camara_comercio` YA EXISTENTE:
--   • Es del catálogo de paridad FLIT 1.0 (23-HU10520) y describe otra cosa: «Cámara de Comercio +
--     cédula representante», sin rol.
--   • Arrastra los datos migrados de V1: `V1AttachmentMap` colapsa en él las TRES llaves del sistema
--     legado (`id_attached_chamber_commerce_seller`, `_buyer` y `_owner`), que es justo la colisión
--     que estos códigos vienen a evitar. Reutilizarlo mezclaría lo migrado con lo nuevo.
-- Por eso los tres códigos llevan sufijo explícito, incluido `_comprador`, apartándose a propósito de
-- la convención de `escritura_representante` (donde el código sin sufijo es el del comprador).
--
-- `is_system_generated` se queda en su DEFAULT false: este documento SE CARGA, no se genera. Con ello
-- no entra en la lista ordenable del OT ni lo retira la limpieza de huérfanos del expediente, que sí
-- barre los tipos de sistema sin mirar el `source`.
--
-- Tampoco se siembra en `tramites.procedure_document_requirements`: igual que la escritura del
-- representante, el documento no pasa por el checklist de Requisitos — se pide y se carga en el paso
-- del actor, junto a los datos de la sociedad que viene a acreditar. El gate del paso es el que
-- bloquea el avance cuando el certificado es obligatorio.
--
-- DDL IDEMPOTENTE (ON CONFLICT DO NOTHING): puede reaplicarse sin efecto.
-- ─────────────────────────────────────────────────────────────────────────────

INSERT INTO tramites.document_types (code, name, description, mime_types_allowed, max_size_bytes, is_active)
VALUES
    ('camara_comercio_vendedor',
     'Cámara de Comercio (vendedor)',
     'Certificado de existencia y representación legal de la sociedad vendedora, expedido por una Cámara de Comercio.',
     '["application/pdf"]', 20971520, true),
    ('camara_comercio_comprador',
     'Cámara de Comercio (comprador)',
     'Certificado de existencia y representación legal de la sociedad compradora, expedido por una Cámara de Comercio.',
     '["application/pdf"]', 20971520, true),
    ('camara_comercio_locatario',
     'Cámara de Comercio (locatario)',
     'Certificado de existencia y representación legal de la sociedad locataria, expedido por una Cámara de Comercio.',
     '["application/pdf"]', 20971520, true)
ON CONFLICT (code) DO NOTHING;

-- Instrucción de carga (102-document-types-upload-instructions añadió la columna). Se repite por rol
-- para que el gestor lea a quién corresponde la casilla que tiene delante.
UPDATE tramites.document_types
   SET upload_instructions = 'Adjunta el certificado de Cámara de Comercio de la sociedad vendedora. Se recomienda que no tenga más de 30 días de expedición.'
 WHERE code = 'camara_comercio_vendedor' AND upload_instructions IS NULL;

UPDATE tramites.document_types
   SET upload_instructions = 'Adjunta el certificado de Cámara de Comercio de la sociedad compradora. Se recomienda que no tenga más de 30 días de expedición.'
 WHERE code = 'camara_comercio_comprador' AND upload_instructions IS NULL;

UPDATE tramites.document_types
   SET upload_instructions = 'Adjunta el certificado de Cámara de Comercio de la sociedad locataria. Se recomienda que no tenga más de 30 días de expedición.'
 WHERE code = 'camara_comercio_locatario' AND upload_instructions IS NULL;
