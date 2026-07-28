-- Feature #10929 (RL-flujo-ajustes) — Escritura DEL REPRESENTANTE. Hoy la escritura se asocia a la
-- EMPRESA (puente M:N admin.company_deed_companies), así que dos representantes que comparten empresa
-- ven las mismas escrituras. Se agrega la referencia (nullable) al representante que la asoció, para
-- que el detalle del representante muestre SOLO sus escrituras y el trámite use la del representante
-- seleccionado. Nullable = compat: las escrituras legadas quedan sin representante (no aparecen en el
-- detalle de ningún representante). ON DELETE CASCADE: al borrar el representante caen sus escrituras.
-- DDL IDEMPOTENTE (ADD COLUMN IF NOT EXISTS + guardas): puede re-aplicarse sin efecto.

ALTER TABLE admin.company_deeds
  ADD COLUMN IF NOT EXISTS representative_id uuid;

-- FK → representante (ON DELETE CASCADE). Guarda idempotente: solo la crea si no existe.
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint WHERE conname = 'fk_company_deeds_representative'
  ) THEN
    ALTER TABLE admin.company_deeds
      ADD CONSTRAINT fk_company_deeds_representative
      FOREIGN KEY (representative_id)
      REFERENCES admin.company_legal_representatives(id)
      ON DELETE CASCADE ON UPDATE CASCADE;
  END IF;
END $$;

-- Índice de FK + filtro del detalle (escrituras del representante dentro del tenant).
CREATE INDEX IF NOT EXISTS ix_company_deeds_tenant_representative
  ON admin.company_deeds(tenant_id, representative_id);

COMMENT ON COLUMN admin.company_deeds.representative_id
  IS 'Feature #10929 — representante (admin.company_legal_representatives.id) que asoció la escritura; NULL en escrituras legadas.';
