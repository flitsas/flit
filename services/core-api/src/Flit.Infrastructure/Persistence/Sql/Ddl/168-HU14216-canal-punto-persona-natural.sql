-- =============================================================================
-- HU #14216 (Épica #14186) — canal del punto de atención de Persona Natural.
-- Migración: 20261029120000_HU14216_CanalPuntoPersonaNatural.
--
-- Tabla GLOBAL, una fila por organismo. NO es el grant «Ofrecer en el portal»
-- (admin.tenant_transit_office_grants): encender o apagar este canal no toca is_enabled de ese grant.
-- Sin tenant_id (excepción análoga a admin.persona_natural_settings): el canal es de la plataforma,
-- uno por organismo de tránsito, no por empresa. Por eso no hay RLS ni política tenant_isolation.
-- Sin datos de ciudadanos: updated_by es el usuario de plataforma que cambió el interruptor.
-- La auditoría del cambio la escribe el caso de uso en admin.tenant_config_audit_logs.
-- El contrato de columnas es el pedido: transit_office_id, is_enabled, updated_at, updated_by.
-- =============================================================================

CREATE TABLE IF NOT EXISTS admin.persona_natural_point_channel (
    transit_office_id uuid NOT NULL,
    CONSTRAINT pk_persona_natural_point_channel PRIMARY KEY (transit_office_id),
    is_enabled boolean NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by uuid NULL,
    CONSTRAINT fk_persona_natural_point_channel_transit_offices FOREIGN KEY (transit_office_id)
        REFERENCES catalogs.transit_offices (id) ON DELETE RESTRICT ON UPDATE RESTRICT
);

COMMENT ON TABLE admin.persona_natural_point_channel IS
    'Canal del punto de atención de Persona Natural (HU #14216). Uno por organismo. Global sin tenant_id y sin PII. Independiente de admin.tenant_transit_office_grants.';
