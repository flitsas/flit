-- HU #10930 — Baúl de firmas: deprecar NIT y agregar código hash | Feature #10929 (RL-flujo-ajustes)
-- La firma del baúl es exclusivamente de la PERSONA y del tenant: el NIT deja de participar en la
-- identidad de la firma. Se depreca nit_empresa (pasa a nullable, sale de obligatorio) y se agrega
-- codigo_hash (código alfanumérico que digita el usuario, DISTINTO del signature_hash — este último
-- es el SHA-256 calculado del artefacto). La unicidad de firma 'activa' pasa de
-- (tenant, NIT, documento) a (tenant, documento). Idempotente (re-ejecutable).

-- Código alfanumérico digitado por el usuario (no es el SHA-256 del artefacto). Nullable.
ALTER TABLE admin.signature_vault ADD COLUMN IF NOT EXISTS codigo_hash varchar(100);

-- El NIT deja de ser obligatorio: la firma es de la persona + tenant (Feature #10929).
ALTER TABLE admin.signature_vault ALTER COLUMN nit_empresa DROP NOT NULL;

-- Nueva invariante: a lo sumo UNA firma 'activa' por (tenant, documento) — el NIT sale de la llave.
-- Se recrea con el MISMO nombre uq_signature_vault_activa (reemplaza la de (tenant, NIT, documento)).
DROP INDEX IF EXISTS admin.uq_signature_vault_activa;
CREATE UNIQUE INDEX IF NOT EXISTS uq_signature_vault_activa
  ON admin.signature_vault(tenant_id, document_number)
  WHERE estado = 'activa';

-- Consulta de consumo por persona (tenant, documento) filtrando por estado.
CREATE INDEX IF NOT EXISTS ix_signature_vault_tenant_document_estado
  ON admin.signature_vault(tenant_id, document_number, estado);

-- El índice viejo por NIT (ix_signature_vault_tenant_id_nit_empresa_estado) se DEJA intacto:
-- el NIT sigue siendo consultable durante la transición (se migra en HUs posteriores).
