-- =============================================================================
-- Clase de la cabeza de grupo como tipo de compañía (CONCESION | MARCA_BLANCA) y padre de la
-- compañía radicadora conservado en el trámite — HU #12406 (Feature #12254, Épica #12235).
-- Migración: 20260910140000_HU12406_HeadTenantTypesAndParentSnapshot
-- Database Agent + Backend Agent. Diseño aprobado por el usuario el 2026-09-10: la clase NO es una
-- columna aparte (se descartó group_kind); es un VALOR de identity.tenants.tenant_type.
--
-- 1. ck_tenants_tenant_type (RestrictTenantTypeCatalog) amplía el catálogo B2B con los dos tipos de
--    cabeza: RENTING | CONCESIONARIO | FLIT | CONCESION | MARCA_BLANCA. DROP IF EXISTS + ADD, como lo
--    hizo la migración original (idempotente). Un OT sigue siendo RENTING; un concesionario suelto
--    sigue siendo CONCESIONARIO: solo la CABEZA de una red lleva CONCESION o MARCA_BLANCA.
--
-- 2. is_group_parent queda ACOPLADO al tipo por el CHECK ck_tenants_group_parent_by_type:
--        is_group_parent = (tenant_type IN ('CONCESION', 'MARCA_BLANCA'))
--    Fail-closed a propósito: quien escriba un tipo de cabeza tiene que escribir is_group_parent =
--    true EN LA MISMA FILA (y false al volver a un tipo que no es de cabeza). No hay trigger que lo
--    corrija en silencio: un UPDATE de tenant_type que olvide is_group_parent se rechaza (AC1/AC2).
--    Así una cabeza siempre declara su clase (no hay cabeza sin tipo de cabeza) y nadie que no sea
--    cabeza puede llevar un tipo de cabeza.
--
-- 3. identity.trg_tenant_hierarchy_depth() (107-HU12318) se AMPLÍA —mismo nombre, CREATE OR
--    REPLACE— con la rama (d), manteniendo (a)(b)(c) intactas:
--      (d) clase inmutable mientras la cabeza tenga hijos vigentes (parent_tenant_id = NEW.id): si
--          cambia tenant_type y el valor viejo o el nuevo es de cabeza, y hay hijos, RAISE. Cubre
--          CONCESION ⇄ MARCA_BLANCA (cambiaría la política de organismos y la marca de toda la red
--          de golpe, AC3) y cabeza → no-cabeza con hijos (que (c) ya rechazaba por is_group_parent;
--          (d) va antes para que el mensaje hable del tipo). Sin hijos, el cambio se acepta.
--    El trigger tr_tenants_hierarchy pasa a dispararse también en UPDATE OF tenant_type.
--
-- 4. tramites.procedure_instances.parent_tenant_id_at_creation uuid NULL — padre que tenía la
--    compañía radicadora en el momento de crear el trámite. EXCEPCIÓN DOCUMENTADA al checklist
--    A7/A8/A9: SIN clave foránea. Es trazabilidad, no integridad referencial: debe sobrevivir al
--    desvínculo (el hijo deja de apuntar al padre), al cambio de cabeza y a un eventual borrado del
--    padre (AC6). Lo escribe la aplicación en el punto de persistencia del comando de creación
--    (ProcedureInstanceRepository.AddWithUniqueReferenceAsync / AdminProcedureInstanceRepository.
--    CreateWithSnapshotAsync) leyendo identity.tenants.parent_tenant_id en el mismo DbContext y en
--    el mismo INSERT que el trámite; NUNCA se recalcula después.
--    Inmutabilidad forzada por la base (AC5): tr_procedure_instances_parent_snapshot_immutable,
--    BEFORE UPDATE OF parent_tenant_id_at_creation. Con "OF <columna>" solo dispara cuando un UPDATE
--    menciona esa columna en el SET; los UPDATE normales del wizard (EF solo manda las columnas
--    modificadas y la propiedad es AfterSaveBehavior.Throw) no lo tocan, así que el coste en la tabla
--    caliente es cero. Sin índice: nadie filtra por esta columna en esta HU.
--
-- Sin poblado: ninguna fila cambia de tipo ni de is_group_parent, y todo trámite existente queda
-- con parent_tenant_id_at_creation NULL (AC8). PRECONDICIÓN (F0, jerarquía vacía): ninguna fila con
-- is_group_parent = true, porque hoy ninguna lleva un tipo de cabeza; si la hubiera, el CHECK 2
-- fallaría con claridad y la migración (transaccional) no dejaría nada a medias.
-- Aditiva, idempotente y reaplicable. Reversible: ver Down de la migración (el Down exige que no
-- queden filas con tipo de cabeza, porque devuelve el catálogo a tres valores).
-- =============================================================================

-- ─────────────────────────────────────────────────────────────────────────────
-- 1. Catálogo de tipos: + CONCESION, MARCA_BLANCA
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE identity.tenants
    DROP CONSTRAINT IF EXISTS ck_tenants_tenant_type;

ALTER TABLE identity.tenants
    ADD CONSTRAINT ck_tenants_tenant_type
    CHECK (tenant_type IN ('RENTING', 'CONCESIONARIO', 'FLIT', 'CONCESION', 'MARCA_BLANCA'));

COMMENT ON CONSTRAINT ck_tenants_tenant_type ON identity.tenants IS
    'Catálogo B2B de tipos de compañía permitidos: RENTING | CONCESIONARIO | FLIT y, desde HU #12406, '
    'los dos tipos de CABEZA de grupo CONCESION | MARCA_BLANCA. Mantener en sync con '
    'Flit.Admin.Domain.Companies.Create.CompanyTenantTypes / HeadTenantTypes.';

-- ─────────────────────────────────────────────────────────────────────────────
-- 2. is_group_parent acoplado al tipo (fail-closed, sin corrección silenciosa)
-- ─────────────────────────────────────────────────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_tenants_group_parent_by_type')
    THEN
        ALTER TABLE identity.tenants
            ADD CONSTRAINT ck_tenants_group_parent_by_type
            CHECK (is_group_parent = (tenant_type IN ('CONCESION', 'MARCA_BLANCA')));
    END IF;
END $$;

COMMENT ON CONSTRAINT ck_tenants_group_parent_by_type ON identity.tenants IS
    'HU #12406: is_group_parent es exactamente "el tipo es de cabeza" (CONCESION | MARCA_BLANCA). '
    'Quien escriba un tipo de cabeza debe escribir is_group_parent = true en la misma fila y false '
    'al salir de él; no hay trigger que lo corrija en silencio.';

COMMENT ON COLUMN identity.tenants.is_group_parent IS
    'true = el cliente es cabeza de grupo y puede tener hijos (HU #12318). Desde HU #12406 vale '
    'exactamente tenant_type IN (CONCESION, MARCA_BLANCA) (ck_tenants_group_parent_by_type): la clase '
    'de la cabeza es su tipo de compañía. Una cabeza de grupo no puede tener padre (tr_tenants_hierarchy).';

-- ─────────────────────────────────────────────────────────────────────────────
-- 3. identity.trg_tenant_hierarchy_depth() ampliada con (d)
-- ─────────────────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION identity.trg_tenant_hierarchy_depth() RETURNS trigger AS $$
DECLARE
    v_parent_is_group_parent boolean;
    v_parent_parent_id uuid;
    v_has_children boolean;
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

    IF TG_OP = 'UPDATE' THEN
        v_has_children := EXISTS (SELECT 1 FROM identity.tenants c WHERE c.parent_tenant_id = NEW.id);

        -- (d) HU #12406: la clase de una cabeza con hijos vigentes es inmutable. Si cambia el tipo y
        --     el viejo o el nuevo es de cabeza, con hijos colgando se rechaza.
        IF NEW.tenant_type IS DISTINCT FROM OLD.tenant_type
           AND (OLD.tenant_type IN ('CONCESION', 'MARCA_BLANCA') OR NEW.tenant_type IN ('CONCESION', 'MARCA_BLANCA'))
           AND v_has_children
        THEN
            RAISE EXCEPTION 'tenant %: tiene hijos vinculados; la clase de la cabeza de grupo (tenant_type) no puede cambiar de % a %', NEW.id, OLD.tenant_type, NEW.tenant_type
                USING ERRCODE = 'check_violation';
        END IF;

        -- (c) Quien ya tiene hijos no puede dejar de ser cabeza de grupo ni recibir un padre.
        IF (NEW.is_group_parent IS NOT TRUE OR NEW.parent_tenant_id IS NOT NULL) AND v_has_children THEN
            RAISE EXCEPTION 'tenant %: tiene hijos vinculados; no puede dejar de ser cabeza de grupo ni recibir un padre', NEW.id
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;

    RETURN NEW;
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_tenants_hierarchy ON identity.tenants;
CREATE TRIGGER tr_tenants_hierarchy
    BEFORE INSERT OR UPDATE OF parent_tenant_id, is_group_parent, tenant_type ON identity.tenants
    FOR EACH ROW EXECUTE FUNCTION identity.trg_tenant_hierarchy_depth();

-- ─────────────────────────────────────────────────────────────────────────────
-- 4. tramites.procedure_instances.parent_tenant_id_at_creation (sin FK) + inmutabilidad
-- ─────────────────────────────────────────────────────────────────────────────
ALTER TABLE tramites.procedure_instances
    ADD COLUMN IF NOT EXISTS parent_tenant_id_at_creation uuid NULL;

CREATE OR REPLACE FUNCTION tramites.trg_procedure_instance_parent_snapshot_immutable() RETURNS trigger AS $$
BEGIN
    IF NEW.parent_tenant_id_at_creation IS DISTINCT FROM OLD.parent_tenant_id_at_creation THEN
        RAISE EXCEPTION 'procedure_instance %: parent_tenant_id_at_creation es inmutable (se fija al crear el trámite y nunca se recalcula)', OLD.id
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END; $$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_procedure_instances_parent_snapshot_immutable ON tramites.procedure_instances;
CREATE TRIGGER tr_procedure_instances_parent_snapshot_immutable
    BEFORE UPDATE OF parent_tenant_id_at_creation ON tramites.procedure_instances
    FOR EACH ROW EXECUTE FUNCTION tramites.trg_procedure_instance_parent_snapshot_immutable();

COMMENT ON COLUMN tramites.procedure_instances.parent_tenant_id_at_creation IS
    'Cabeza de grupo (identity.tenants.id) de la que colgaba la compañía radicadora al crear el '
    'trámite (HU #12406, Épica #12235). NULL = la compañía no tenía padre. SIN FK a propósito '
    '(trazabilidad, no integridad referencial): sobrevive al desvínculo, al cambio de cabeza y al '
    'borrado del padre. Se escribe en el mismo INSERT que el trámite y es inmutable '
    '(tr_procedure_instances_parent_snapshot_immutable).';
