-- ─────────────────────────────────────────────────────────────────────────────
-- Migración V1 → V2 — tipos de documento que solo existían en V1.
--
-- V1 arma su expediente en caliente y no lo guarda: cada documento se genera cuando
-- alguien lo pide y se descarta. Al migrar un trámite hay que materializar esas piezas
-- (--tipo transfer-documents), y para guardarlas hace falta que su tipo exista en el
-- catálogo de V2.
--
-- El catálogo ya cubre casi todo (portada, fur, compraventa, rues, mandato,
-- tramite_virtual, certificado_vigencia_soat_rtm, impronta_validada, cedulas,
-- transferencia_dominio, poder_tramitador, consolidado). Faltaban estos tres.
--
-- Idempotente: ON CONFLICT (code) DO NOTHING, para no pisar la parametrización que un
-- admin haya ajustado después.
-- ─────────────────────────────────────────────────────────────────────────────
INSERT INTO tramites.document_types (code, name, description, mime_types_allowed, max_size_bytes, is_active)
VALUES
    ('limitacion_propiedad',
     'Limitación de la propiedad y garantía a favor de',
     'Documento de limitación a la propiedad y garantía a favor del acreedor prendario (generado por FLIT 1.0)',
     '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('carta_declaratoria',
     'Carta declaratoria',
     'Carta declaratoria del traspaso unilateral (generada por FLIT 1.0)',
     '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('autorizacion_apoderado',
     'Autorización al apoderado',
     'Poder de autorización al apoderado registrado por la compañía (generado por FLIT 1.0)',
     '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true)
ON CONFLICT (code) DO NOTHING;
