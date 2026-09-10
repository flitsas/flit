-- HU #11204 (Feature #11191, otros-ajustes) — Familias de mandatario y datos por organismo.
--
-- Hasta ahora dar de alta un organismo con formato de mandato equivalente a uno ya soportado exigía
-- tocar código: el template_code tenía un CHECK cerrado y los datos propios del OT (ciudad de la
-- Cámara de Comercio, sigla de la unión temporal) estaban incrustados en el generador. Con esto, un
-- organismo nuevo es una FILA: elige la redacción equivalente y aporta sus datos.
--
-- Las redacciones NO se colapsan. Las plantillas del PO marcan a Bello y Sabaneta con la misma
-- `familia_mandatario: organismo_transito`, pero su texto legal difiere: Bello nombra al REPRESENTANTE
-- LEGAL de la unión temporal y Sabaneta nombra a la unión temporal directamente; la cláusula de
-- obligaciones usa la razón social en una y la sigla en la otra. La familia describe QUIÉN es el
-- mandatario (una persona o el propio organismo), no cómo está redactado el contrato.
-- DDL IDEMPOTENTE.

-- ── El template_code deja de ser un catálogo cerrado ─────────────────────────────
-- Con los datos del OT en configuración, asignarle a un organismo nuevo una redacción ya soportada
-- deja de ser un cambio de código, así que el CHECK ya no protege nada: solo estorba.
ALTER TABLE admin.transit_office_mandate_config
  DROP CONSTRAINT IF EXISTS ck_transit_office_mandate_config_template;

-- ── Familia del mandatario + datos propios del organismo ────────────────────────
ALTER TABLE admin.transit_office_mandate_config
  ADD COLUMN IF NOT EXISTS mandatary_family varchar(30) NOT NULL DEFAULT 'individuo',
  -- Ciudad de la Cámara de Comercio que acredita la representación del MANDANTE.
  ADD COLUMN IF NOT EXISTS chamber_city varchar(120),
  -- Sigla de la unión temporal (p. ej. UT-SETSA), usada en la cláusula de obligaciones.
  ADD COLUMN IF NOT EXISTS mandatary_sigla varchar(60);

ALTER TABLE admin.transit_office_mandate_config
  DROP CONSTRAINT IF EXISTS ck_transit_office_mandate_config_family;
ALTER TABLE admin.transit_office_mandate_config
  ADD CONSTRAINT ck_transit_office_mandate_config_family
  CHECK (mandatary_family IN ('individuo', 'organismo_transito'));

-- ── Backfill: la familia se deduce de lo que hoy hace cada OT ────────────────────
-- Los dos OT con unión temporal como mandatario son `organismo_transito`; el resto, `individuo`.
UPDATE admin.transit_office_mandate_config
SET mandatary_family = 'organismo_transito'
WHERE template_code IN ('sabaneta', 'bello')
  AND mandatary_family <> 'organismo_transito';

-- Datos que hasta ahora estaban incrustados en el generador. Se escriben solo si están vacíos, para
-- no pisar una configuración que alguien ya haya ajustado a mano.
UPDATE admin.transit_office_mandate_config
SET chamber_city = COALESCE(chamber_city, 'Medellín'),
    mandatary_sigla = CASE
        WHEN template_code = 'sabaneta' THEN COALESCE(mandatary_sigla, 'UT-SETSA')
        ELSE mandatary_sigla
    END;
