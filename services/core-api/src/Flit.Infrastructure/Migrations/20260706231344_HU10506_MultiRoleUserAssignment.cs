using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10506_MultiRoleUserAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invitation_roles",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invitation_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_invitation_roles_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "security",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_invitation_roles_user_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalSchema: "security",
                        principalTable: "user_invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invitation_roles_invitation_id",
                schema: "security",
                table: "invitation_roles",
                column: "invitation_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_roles_role_id",
                schema: "security",
                table: "invitation_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_invitation_roles_tenant_id_invitation_id_role_id",
                schema: "security",
                table: "invitation_roles",
                columns: new[] { "tenant_id", "invitation_id", "role_id" },
                unique: true);

            // FK a identity.tenants + RLS + triggers estándar (checklist A4/A8/A10/A16):
            // EF Fluent API no modela tenant_id como una relación de navegación (mismo patrón
            // que user_invitations/user_role_assignments, cuyo FK a tenants tampoco se declara
            // vía Fluent API), así que se agrega en SQL crudo, igual que RLS y los triggers de
            // row_version/audit_log — artefactos de BD que EF no puede generar por convención.
            migrationBuilder.Sql(
                """
                ALTER TABLE security.invitation_roles
                    ADD CONSTRAINT fk_invitation_roles_tenants_tenant_id
                    FOREIGN KEY (tenant_id) REFERENCES identity.tenants(id)
                    ON DELETE RESTRICT ON UPDATE CASCADE;

                ALTER TABLE security.invitation_roles ENABLE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON security.invitation_roles
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

                DROP TRIGGER IF EXISTS tr_invitation_roles_row_version ON security.invitation_roles;
                CREATE TRIGGER tr_invitation_roles_row_version BEFORE UPDATE ON security.invitation_roles
                    FOR EACH ROW EXECUTE FUNCTION public.trg_row_version();
                DROP TRIGGER IF EXISTS tr_invitation_roles_audit ON security.invitation_roles;
                CREATE TRIGGER tr_invitation_roles_audit AFTER INSERT OR UPDATE OR DELETE ON security.invitation_roles
                    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
                """);

            // HU #10506: modelo aditivo multi-rol — el índice único parcial de
            // 20260624193655_Fix_UserRoleAssignment_UniqueConstraint (uq_ura_active_user_tenant,
            // sobre (user_id, tenant_id)) solo permitía UN rol activo por usuario/tenant. Se
            // reemplaza por uno que agrega role_id: permite N roles activos simultáneos por
            // (user, tenant), pero sigue impidiendo duplicar el MISMO rol dos veces (AC2).
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS security.uq_ura_active_user_tenant;

                CREATE UNIQUE INDEX IF NOT EXISTS uq_ura_active_user_tenant_role
                    ON security.user_role_assignments(user_id, tenant_id, role_id)
                    WHERE deleted_at IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS security.uq_ura_active_user_tenant_role;

                CREATE UNIQUE INDEX IF NOT EXISTS uq_ura_active_user_tenant
                    ON security.user_role_assignments(user_id, tenant_id)
                    WHERE deleted_at IS NULL;
                """);

            migrationBuilder.DropTable(
                name: "invitation_roles",
                schema: "security");
        }
    }
}
