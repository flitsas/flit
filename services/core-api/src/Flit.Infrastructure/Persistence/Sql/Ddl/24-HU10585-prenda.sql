-- HU #10594 — Dominio de prenda (IT-3) | Feature #10585 (R4/R10/R17 + marcación FUR)
-- La prenda (gravamen) es un agregado COMPAÑERO de la instancia de trámite, no una tipología nueva.
-- Se declara en matrícula (R4), es gate en traspaso (R10) y se puede modificar post-registro (R17).
-- Versionado intrínseco por `estado`: la modificación crea una fila nueva 'vigente' y marca la anterior
-- 'reemplazada' (historial completo). Al vivir en su propia tabla, no toca la inmutabilidad de field_values.

CREATE TABLE tramites.procedure_instance_prenda (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_procedure_instance_prenda PRIMARY KEY (id),
    tenant_id uuid NOT NULL REFERENCES identity.tenants(id) ON DELETE RESTRICT ON UPDATE CASCADE,
    procedure_instance_id uuid NOT NULL REFERENCES tramites.procedure_instances(id) ON DELETE CASCADE ON UPDATE CASCADE,
    decision varchar(20) NOT NULL
        CONSTRAINT ck_procedure_instance_prenda_decision
        CHECK (decision IN ('solicitar', 'registrar', 'levantar', 'omitir', 'sin_prenda')),
    estado varchar(20) NOT NULL DEFAULT 'vigente'
        CONSTRAINT ck_procedure_instance_prenda_estado
        CHECK (estado IN ('vigente', 'reemplazada')),
    acreedor_nombre varchar(200),
    acreedor_documento varchar(20),
    metadata jsonb NOT NULL DEFAULT '{}',
    row_version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid
);
CREATE INDEX ix_procedure_instance_prenda_instance
  ON tramites.procedure_instance_prenda(procedure_instance_id);
CREATE INDEX ix_procedure_instance_prenda_tenant_id
  ON tramites.procedure_instance_prenda(tenant_id);

-- Invariante IT-3: a lo sumo UNA prenda 'vigente' por instancia (el versionado R17 se apoya en esto).
CREATE UNIQUE INDEX uq_procedure_instance_prenda_vigente
  ON tramites.procedure_instance_prenda(procedure_instance_id)
  WHERE estado = 'vigente';

COMMENT ON COLUMN tramites.procedure_instance_prenda.acreedor_documento IS '@pii:medium';

-- RLS por tenant, idéntico a las tablas de trámite runtime (procedure_instances/actors/field_values).
ALTER TABLE tramites.procedure_instance_prenda ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON tramites.procedure_instance_prenda
  USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

DROP TRIGGER IF EXISTS tr_procedure_instance_prenda_row_version ON tramites.procedure_instance_prenda;
CREATE TRIGGER tr_procedure_instance_prenda_row_version BEFORE UPDATE ON tramites.procedure_instance_prenda
  FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
DROP TRIGGER IF EXISTS tr_procedure_instance_prenda_audit ON tramites.procedure_instance_prenda;
CREATE TRIGGER tr_procedure_instance_prenda_audit AFTER INSERT OR UPDATE OR DELETE ON tramites.procedure_instance_prenda
  FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
