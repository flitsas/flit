-- Tipo de mandato (signer | institutional | open) por compañía gestora × OT.
-- La plantilla del PDF (template_code / custom) sigue en transit_office_mandate_config.
-- DDL IDEMPOTENTE.

CREATE TABLE IF NOT EXISTS admin.company_ot_mandate_rules (
    id uuid NOT NULL DEFAULT uuidv7(),
    CONSTRAINT pk_company_ot_mandate_rules PRIMARY KEY (id),
    company_tenant_id uuid NOT NULL
        CONSTRAINT fk_comr_tenant
        REFERENCES identity.tenants(id) ON DELETE CASCADE ON UPDATE CASCADE,
    transit_office_id uuid NOT NULL,
    assignment_mode varchar(20) NOT NULL DEFAULT 'signer',
    mandatary_family varchar(40) NOT NULL DEFAULT 'individuo',
    institutional_mandatary_name varchar(300),
    institutional_mandatary_nit varchar(30),
    chamber_city varchar(120),
    mandatary_sigla varchar(40),
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamptz,
    updated_by uuid,
    CONSTRAINT ck_company_ot_mandate_rules_assignment_mode
        CHECK (assignment_mode IN ('signer', 'institutional', 'open')),
    CONSTRAINT ck_company_ot_mandate_rules_family
        CHECK (mandatary_family IN ('individuo', 'organismo_transito'))
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_company_ot_mandate_rules
  ON admin.company_ot_mandate_rules(company_tenant_id, transit_office_id);

CREATE INDEX IF NOT EXISTS ix_company_ot_mandate_rules_office
  ON admin.company_ot_mandate_rules(transit_office_id);

-- Conservar comportamiento previo: OTs con assignment_mode ≠ signer
-- propagan esa regla a todas las compañías con grant habilitado.
INSERT INTO admin.company_ot_mandate_rules (
    company_tenant_id,
    transit_office_id,
    assignment_mode,
    mandatary_family,
    institutional_mandatary_name,
    institutional_mandatary_nit,
    chamber_city,
    mandatary_sigla)
SELECT
    g.tenant_id,
    cfg.transit_office_id,
    cfg.assignment_mode,
    cfg.mandatary_family,
    cfg.institutional_mandatary_name,
    cfg.institutional_mandatary_nit,
    cfg.chamber_city,
    cfg.mandatary_sigla
FROM admin.transit_office_mandate_config cfg
INNER JOIN admin.tenant_transit_office_grants g
  ON g.transit_office_id = cfg.transit_office_id
 AND g.is_enabled = true
WHERE cfg.assignment_mode IN ('institutional', 'open')
ON CONFLICT (company_tenant_id, transit_office_id) DO NOTHING;

COMMENT ON TABLE admin.company_ot_mandate_rules IS
  'Tipo de mandato (3) por compañía gestora × OT. Plantilla PDF permanece en config del OT.';
