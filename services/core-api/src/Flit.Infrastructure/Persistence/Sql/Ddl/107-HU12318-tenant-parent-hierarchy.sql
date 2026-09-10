-- =============================================================================
-- Jerarquía de clientes padre-hija con profundidad 2 — HU #12318 (Feature #12254, Épica #12235).
-- Migración: 20260910120000_HU12318_TenantParentHierarchy
-- Database Agent.
--
-- Contexto (docs/analisis-jerarquia-companias-concesionario.md §5.2 y §8.1 D10/D12): un cliente
-- cabeza de grupo (Concesión-compañía o Concesión-OT) tiene clientes hijos. Un hijo NO puede tener
-- hijos: profundidad máxima 2, forzada por la base de datos y no por la aplicación.
--
-- Se descartaron la tabla puente con vigencia (un valid_to mal puesto deja visible a un ex-hijo)
-- y la closure table (sobre-diseño). Un cliente tiene a lo sumo UN padre: columna escalar.
--
-- Columnas nuevas sobre identity.tenants (tabla ya existente, 01-HU10146):
--   - parent_tenant_id uuid NULL      → el padre del cliente. NULL = sin padre (mundo actual).
--   - is_group_parent boolean NOT NULL DEFAULT false → capacidad de ser cabeza de grupo. Vive en su
--     PROPIA columna y no en tenant_type: así un OT (tenant_type='RENTING' forzado por
--     TransitOfficeTenantWriteRepository) puede ser cabeza de grupo sin dejar de ser RENTING.
--
-- `tenant_type` y su CHECK ck_tenants_tenant_type (RENTING, CONCESIONARIO, FLIT) NO SE TOCAN (D10).
--
-- Invariantes forzados por la base (no por la aplicación):
--   FK  fk_tenants_parent_tenant  ON DELETE RESTRICT → no se borra un padre con hijos.
--   CK  ck_tenants_parent_not_self → un cliente no es su propio padre.
--   TR  tr_tenants_hierarchy (BEFORE INSERT OR UPDATE OF parent_tenant_id, is_group_parent):
--     (a) si NEW.parent_tenant_id IS NOT NULL: el padre existe, es cabeza de grupo
--         (is_group_parent = true) y NO tiene padre a su vez; y NEW.is_group_parent = false.
--     (b) si NEW.is_group_parent = true: NEW.parent_tenant_id IS NULL.
--     (c) si NEW deja de ser cabeza de grupo o recibe un padre, NO puede tener hijos ya
--         vinculados. Sin (c) la profundidad 2 sería burlable en dos pasos (desmarcar la cabeza
--         y luego colgarla de otra), así que (c) es lo que hace que el límite lo fuerce la base.
--   Todos los rechazos salen como RAISE EXCEPTION con ERRCODE = 'check_violation' (23514), el
--   mismo que usan los demás triggers de invariantes del repo, para que la capa de aplicación los
--   traduzca igual que un CHECK.
--
-- Sin backfill: ninguna fila existente se modifica. Los defaults (NULL / false) reproducen
-- exactamente el mundo actual, así que la migración sale a producción con la jerarquía vacía.
--
-- Aditiva, idempotente y reaplicable. Reversible: ver Down de la migración.
-- =============================================================================

ALTER TABLE identity.tenants
    ADD COLUMN IF NOT EXISTS parent_tenant_id uuid NULL;

ALTER TABLE identity.tenants
    ADD COLUMN IF NOT EXISTS is_group_parent boolean NOT NULL DEFAULT false;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'fk_tenants_parent_tenant')
    THEN
        ALTER TABLE identity.tenants
            ADD CONSTRAINT fk_tenants_parent_tenant
            FOREIGN KEY (parent_tenant_id) REFERENCES identity.tenants(id)
            ON DELETE RESTRICT ON UPDATE CASCADE;
    END IF;
END $$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_tenants_parent_not_self')
    THEN
        ALTER TABLE identity.tenants
            ADD CONSTRAINT ck_tenants_parent_not_self
            CHECK (parent_tenant_id IS NULL OR parent_tenant_id <> id);
    END IF;
END $$;

-- Sostiene "hijos de este padre" y la verificación (c) del trigger. Parcial: la inmensa mayoría de
-- las filas no tiene padre y no aporta nada al índice.
CREATE INDEX IF NOT EXISTS ix_tenants_parent_tenant_id
    ON identity.tenants (parent_tenant_id)
    WHERE parent_tenant_id IS NOT NULL;

CREATE OR REPLACE FUNCTION identity.trg_tenant_hierarchy_depth() RETURNS trigger AS $$
DECLARE
    v_parent_is_group_parent boolean;
    v_parent_parent_id uuid;
BEGIN
    -- (b) Una cabeza de grupo no cuelga de nadie.
    IF NEW.is_group_parent IS TRUE AND NEW.parent_tenant_id IS NOT NULL THEN
        RAISE EXCEPTION 'tenant %: una cabeza de grupo (is_group_parent) no puede tener padre (parent_tenant_id)', NEW.id
            USING ERRCODE = 'check_violation';
    END IF;

    -- (a) Un hijo cuelga de un padre que existe, es cabeza de grupo y no tiene padre a su vez.
    IF NEW.parent_tenant_id IS NOT NULL THEN
        SELECT t.is_group_parent, t.parent_tenant_id
          INTO v_parent_is_group_parent, v_parent_parent_id
          FROM identity.tenants t
         WHERE t.id = NEW.parent_tenant_id;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'tenant %: el padre % no existe', NEW.id, NEW.parent_tenant_id
                USING ERRCODE = 'check_violation';
        END IF;

        IF v_parent_is_group_parent IS NOT TRUE THEN
            RAISE EXCEPTION 'tenant %: el padre % no es cabeza de grupo (is_group_parent = false)', NEW.id, NEW.parent_tenant_id
                USING ERRCODE = 'check_violation';
        END IF;

        IF v_parent_parent_id IS NOT NULL THEN
            RAISE EXCEPTION 'tenant %: el padre % ya tiene padre; profundidad máxima 2', NEW.id, NEW.parent_tenant_id
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;

    -- (c) Quien ya tiene hijos no puede dejar de ser cabeza de grupo ni recibir un padre.
    IF TG_OP = 'UPDATE'
       AND (NEW.is_group_parent IS NOT TRUE OR NEW.parent_tenant_id IS NOT NULL)
       AND EXISTS (SELECT 1 FROM identity.tenants c WHERE c.parent_tenant_id = NEW.id)
    THEN
        RAISE EXCEPTION 'tenant %: tiene hijos vinculados; no puede dejar de ser cabeza de grupo ni recibir un padre', NEW.id
            USING ERRCODE = 'check_violation';
    END IF;

    RETURN NEW;
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_tenants_hierarchy ON identity.tenants;
CREATE TRIGGER tr_tenants_hierarchy
    BEFORE INSERT OR UPDATE OF parent_tenant_id, is_group_parent ON identity.tenants
    FOR EACH ROW EXECUTE FUNCTION identity.trg_tenant_hierarchy_depth();

COMMENT ON COLUMN identity.tenants.parent_tenant_id IS
    'Cabeza de grupo de la que cuelga este cliente (HU #12318, Épica #12235). NULL = sin padre. '
    'Un único padre por cliente (columna escalar, sin tabla puente). FK identity.tenants(id) '
    'ON DELETE RESTRICT. Profundidad máxima 2 forzada por tr_tenants_hierarchy.';

COMMENT ON COLUMN identity.tenants.is_group_parent IS
    'true = el cliente es cabeza de grupo y puede tener hijos (HU #12318). Independiente de '
    'tenant_type: un OT RENTING puede ser cabeza de grupo. Una cabeza de grupo no puede tener '
    'padre (tr_tenants_hierarchy).';

COMMENT ON CONSTRAINT ck_tenants_parent_not_self ON identity.tenants IS
    'Un cliente no puede ser su propio padre (HU #12318).';
