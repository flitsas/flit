-- HU #11201 (Feature #11190, otros-ajustes) — Un mandatario para varios organismos de tránsito.
--
-- Hasta ahora el organismo vivía EN el mandatario (admin.mandate_signers.transit_office_id), así que
-- una misma persona que firma en tres organismos eran tres filas distintas: tres documentos, tres
-- firmas del baúl, tres validaciones de identidad que renovar por separado. La relación se mueve a un
-- puente y la persona pasa a existir UNA sola vez.
--
-- mandate_signers.transit_office_id se conserva como organismo PRIMARIO y queda deprecada —igual que
-- represented_company_id en la HU #10932—: sigue poblada para no romper lecturas que aún la consulten,
-- pero la fuente de verdad de "en qué organismos aplica este mandatario" pasa a ser el puente. No se
-- relaja su NOT NULL a propósito: siempre hay un organismo primario que escribir, y aflojar la
-- restricción solo introduciría una divergencia entre el esquema y el modelo EF sin ganar nada.
-- DDL IDEMPOTENTE.

-- ── Puente mandatario ↔ organismo de tránsito ───────────────────────────────────
CREATE TABLE IF NOT EXISTS admin.mandate_signer_transit_offices (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_mandate_signer_transit_offices PRIMARY KEY (id),
    mandate_signer_id uuid NOT NULL
        CONSTRAINT fk_msto_mandate_signer
        REFERENCES admin.mandate_signers(id) ON DELETE CASCADE ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL,
    -- Baja lógica, igual que en mandate_signer_companies: retirar un organismo conserva el histórico
    -- y libera la unicidad. Al inactivar el mandatario, sus organismos quedan is_active = false.
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now()
);

-- Una sola asignación ACTIVA por (mandatario, organismo). Filtrado por is_active para que retirar y
-- volver a agregar el mismo organismo no choque con el histórico.
CREATE UNIQUE INDEX IF NOT EXISTS uq_mandate_signer_transit_offices_active
  ON admin.mandate_signer_transit_offices(mandate_signer_id, transit_office_id)
  WHERE is_active;

-- Lectura por organismo: "qué mandatarios aplican en este OT" es la consulta del trámite.
CREATE INDEX IF NOT EXISTS ix_mandate_signer_transit_offices_office
  ON admin.mandate_signer_transit_offices(transit_office_id, is_active);

-- ── Backfill: cada mandatario existente aporta su organismo actual (AC4) ─────────
-- El estado del vínculo hereda el del mandatario: uno inactivo no debe reaparecer como disponible.
INSERT INTO admin.mandate_signer_transit_offices (mandate_signer_id, transit_office_id, is_active, created_at)
SELECT s.id, s.transit_office_id, s.is_active, s.created_at
FROM admin.mandate_signers s
WHERE s.transit_office_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM admin.mandate_signer_transit_offices x
    WHERE x.mandate_signer_id = s.id AND x.transit_office_id = s.transit_office_id);
