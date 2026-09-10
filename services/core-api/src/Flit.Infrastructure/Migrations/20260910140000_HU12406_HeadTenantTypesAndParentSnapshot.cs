using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12406 (Feature #12254, Épica #12235) — clase de la cabeza de grupo como tipo de compañía y
/// padre de la compañía radicadora conservado en el trámite. Amplía el CHECK
/// <c>ck_tenants_tenant_type</c> con <c>CONCESION</c> y <c>MARCA_BLANCA</c>, acopla
/// <c>is_group_parent</c> al tipo (<c>ck_tenants_group_parent_by_type</c>, fail-closed), amplía
/// <c>identity.trg_tenant_hierarchy_depth()</c> con la rama «clase inmutable mientras la cabeza tenga
/// hijos vigentes» (dispara también en <c>UPDATE OF tenant_type</c>) y agrega
/// <c>tramites.procedure_instances.parent_tenant_id_at_creation</c> (uuid NULL, SIN FK a propósito:
/// trazabilidad) protegida por <c>tr_procedure_instances_parent_snapshot_immutable</c>.
/// Sin poblado: ninguna fila cambia. DDL: <c>109-HU12406-head-tenant-types-and-parent-snapshot.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260910140000_HU12406_HeadTenantTypesAndParentSnapshot")]
public partial class HU12406_HeadTenantTypesAndParentSnapshot : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("109-HU12406-head-tenant-types-and-parent-snapshot.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Orden inverso al Up. Primero el trigger de inmutabilidad y la columna del trámite; después se
    /// RESTAURA la función <c>identity.trg_tenant_hierarchy_depth()</c> tal como la dejó
    /// <c>107-HU12318</c> (ramas (a)(b)(c)) y su trigger con la lista de columnas original; luego el
    /// CHECK de acoplamiento; y por último el catálogo de tipos vuelve a sus tres valores
    /// (<c>RestrictTenantTypeCatalog</c>). Ese último paso exige que no quede ninguna fila con tipo
    /// de cabeza: revertir esta HU con cabezas declaradas falla a propósito en vez de reescribir
    /// tipos por debajo.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_procedure_instances_parent_snapshot_immutable ON tramites.procedure_instances;

            DROP FUNCTION IF EXISTS tramites.trg_procedure_instance_parent_snapshot_immutable();

            ALTER TABLE tramites.procedure_instances
                DROP COLUMN IF EXISTS parent_tenant_id_at_creation;

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

            COMMENT ON COLUMN identity.tenants.is_group_parent IS
                'true = el cliente es cabeza de grupo y puede tener hijos (HU #12318). Independiente de '
                'tenant_type: un OT RENTING puede ser cabeza de grupo. Una cabeza de grupo no puede tener '
                'padre (tr_tenants_hierarchy).';

            ALTER TABLE identity.tenants
                DROP CONSTRAINT IF EXISTS ck_tenants_group_parent_by_type;

            ALTER TABLE identity.tenants
                DROP CONSTRAINT IF EXISTS ck_tenants_tenant_type;

            ALTER TABLE identity.tenants
                ADD CONSTRAINT ck_tenants_tenant_type
                CHECK (tenant_type IN ('RENTING', 'CONCESIONARIO', 'FLIT'));

            COMMENT ON CONSTRAINT ck_tenants_tenant_type ON identity.tenants IS
                'Catálogo B2B de tipos de compañía permitidos. Mantener en sync con Flit.Admin.Domain.Companies.Create.CompanyTenantTypes.';
            """);
}
