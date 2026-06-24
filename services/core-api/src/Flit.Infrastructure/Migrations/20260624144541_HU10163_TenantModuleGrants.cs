using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10163_TenantModuleGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS security.tenant_module_grants (
                    id          uuid                     NOT NULL DEFAULT uuidv7(),
                    tenant_id   uuid                     NOT NULL,
                    module_id   uuid                     NOT NULL,
                    granted_at  timestamp with time zone NOT NULL DEFAULT now(),
                    granted_by  uuid,
                    CONSTRAINT pk_tenant_module_grants PRIMARY KEY (id),
                    CONSTRAINT fk_tmg_module FOREIGN KEY (module_id)
                        REFERENCES security.modules (id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ix_tenant_module_grants_tenant_id_module_id
                    ON security.tenant_module_grants (tenant_id, module_id);
                CREATE INDEX IF NOT EXISTS ix_tenant_module_grants_module_id
                    ON security.tenant_module_grants (module_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_module_grants",
                schema: "security");
        }
    }
}
