using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU14587_ProductRoleSuspensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_role_suspensions",
                schema: "security",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Rol del producto que tenía al suspenderse; se recrea al reactivar."),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    suspended_by = table.Column<Guid>(type: "uuid", nullable: false, comment: "Usuario que suspendió (@pii:low).")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_role_suspensions", x => x.id);
                    table.CheckConstraint("ck_product_role_suspensions_product_code", "product_code <> ''");
                    table.ForeignKey(
                        name: "fk_product_role_suspensions_products",
                        column: x => x.product_code,
                        principalSchema: "platform",
                        principalTable: "products",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_role_suspensions_roles",
                        column: x => x.role_id,
                        principalSchema: "security",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_role_suspensions_tenants",
                        column: x => x.tenant_id,
                        principalSchema: "identity",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_role_suspensions_users",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Suspensión de un usuario en un producto (ADR-0089): la asignación del rol del producto se cierra y esta fila permite listarlo como SUSPENDIDO y reactivarlo con el mismo rol. No suspende la cuenta.");

            migrationBuilder.CreateIndex(
                name: "ix_product_role_suspensions_product_code",
                schema: "security",
                table: "product_role_suspensions",
                column: "product_code");

            migrationBuilder.CreateIndex(
                name: "ix_product_role_suspensions_role_id",
                schema: "security",
                table: "product_role_suspensions",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_product_role_suspensions_user_id",
                schema: "security",
                table: "product_role_suspensions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_product_role_suspensions",
                schema: "security",
                table: "product_role_suspensions",
                columns: new[] { "tenant_id", "user_id", "product_code" },
                unique: true);

            // ADR-0089 (HU #14587): mismo régimen que las demás tablas con tenant_id del esquema security (RLS + tenant_isolation)
            // y rastro en audit.audit_logs de cada suspensión (INSERT) y reactivación (DELETE): la fila es una marca que se
            // borra al reactivar, así que no lleva row_version ni soft delete (excepción A5/A6/A16 documentada en el ADR).
            migrationBuilder.Sql(
                """
                ALTER TABLE security.product_role_suspensions ENABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON security.product_role_suspensions;
                CREATE POLICY tenant_isolation ON security.product_role_suspensions
                    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);

                DROP TRIGGER IF EXISTS tr_product_role_suspensions_audit ON security.product_role_suspensions;
                CREATE TRIGGER tr_product_role_suspensions_audit AFTER INSERT OR UPDATE OR DELETE ON security.product_role_suspensions
                    FOR EACH ROW EXECUTE FUNCTION public.trg_audit_log();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tr_product_role_suspensions_audit ON security.product_role_suspensions;
                DROP POLICY IF EXISTS tenant_isolation ON security.product_role_suspensions;
                """);

            migrationBuilder.DropTable(
                name: "product_role_suspensions",
                schema: "security");
        }
    }
}
