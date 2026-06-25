using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Fix_UserRoleAssignment_UniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sync model drift: transit_offices was created via raw SQL DDL but missing from EF history
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS catalogs.transit_offices (
                    id uuid NOT NULL,
                    code character varying(10) NOT NULL,
                    name character varying(200) NOT NULL,
                    department_code character varying(2) NOT NULL,
                    city_code character varying(5) NOT NULL,
                    is_active boolean NOT NULL,
                    CONSTRAINT pk_transit_offices PRIMARY KEY (id)
                );
                """);

            // Fix ASSIGN_ROLE_ERROR: replace full unique constraint with a partial index
            // that only enforces uniqueness among active (non-deleted) assignments.
            // Without this, soft-deleting an assignment then creating a new one fails.
            migrationBuilder.Sql(
                """
                ALTER TABLE security.user_role_assignments
                    DROP CONSTRAINT IF EXISTS uq_user_role_assignments_user_id_tenant_id;

                CREATE UNIQUE INDEX IF NOT EXISTS uq_ura_active_user_tenant
                    ON security.user_role_assignments(user_id, tenant_id)
                    WHERE deleted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS security.uq_ura_active_user_tenant;

                ALTER TABLE security.user_role_assignments
                    ADD CONSTRAINT uq_user_role_assignments_user_id_tenant_id
                    UNIQUE (user_id, tenant_id);
                """);

            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS catalogs.transit_offices;
                """);
        }
    }
}
