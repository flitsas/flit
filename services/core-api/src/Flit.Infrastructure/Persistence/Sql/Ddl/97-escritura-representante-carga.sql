-- ─────────────────────────────────────────────────────────────────────────────
-- Escritura del representante legal CARGADA POR EL GESTOR.
--
-- Hasta ahora la escritura de una parte jurídica solo tenía una vía: la resuelve el sistema contra
-- el directorio de la compañía (`admin.company_deeds`, ADR-0033 / HU #10926) y la adjunta como
-- 'escritura' / 'escritura_comprador' con Source=system. Eso deja sin salida al caso en que el
-- gestor cambia el representante legal precargado por una persona que NO está en el módulo de
-- representantes: no hay escritura suya en el directorio, así que no hay nada que apalancar y el
-- trámite se radicaba sin el documento que acredita a quien firma.
--
-- Estos tipos son la vía MANUAL para ese caso. Son DISTINTOS de 'escritura'/'escritura_comprador' a
-- propósito:
--   • Aquellos son `is_system_generated = true` ⇒ el checklist del gestor los excluye
--     (ChecklistEngine.ExcludeFromGestorCarga) y no se pueden cargar.
--   • Y la limpieza de huérfanos del expediente retira adjuntos de ESOS tipos sin mirar el `source`,
--     así que una carga manual bajo el mismo código se borraría en la siguiente regeneración.
-- Con código propio, el adjunto automático queda intacto y el manual es —también en el catálogo—
-- otra cosa.
--
-- `is_system_generated = false`: se carga, no se genera. Al no estar tampoco en la matriz
-- (tramites.procedure_document_requirements) no entra al checklist de Requisitos ni a la lista
-- ordenable del OT: se pide y se carga en el paso de Actores, junto a los datos del representante
-- que es justo lo que viene a acreditar.
--
-- Un tipo por rol, misma convención que 'certificado_identidad{_rol}': así las dos partes de un
-- traspaso con representante fuera del directorio pueden cargar cada una la suya sin pisarse.
--
-- DDL IDEMPOTENTE (ON CONFLICT DO NOTHING): puede re-aplicarse sin efecto.
-- ─────────────────────────────────────────────────────────────────────────────

INSERT INTO tramites.document_types (code, name, description, mime_types_allowed, max_size_bytes, is_active)
VALUES
    ('escritura_representante',
     'Escritura del representante legal',
     'Escritura o poder que acredita al representante legal capturado en el trámite cuando no está registrado en el módulo de representantes legales de la compañía.',
     '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('escritura_representante_vendedor',
     'Escritura del representante legal (vendedor)',
     'Escritura o poder del representante legal de la parte vendedora cuando no está registrado en el módulo de representantes legales de la compañía.',
     '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true),
    ('escritura_representante_locatario',
     'Escritura del representante legal (locatario)',
     'Escritura o poder del representante legal del arrendatario cuando no está registrado en el módulo de representantes legales de la compañía.',
     '["application/pdf","image/jpeg","image/png","image/webp"]', 20971520, true)
ON CONFLICT (code) DO NOTHING;
