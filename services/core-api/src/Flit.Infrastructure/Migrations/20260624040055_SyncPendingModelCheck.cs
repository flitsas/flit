using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Sincroniza el snapshot EF con entidades admin OT (DDL 09-HU10152) y asegura
    /// columnas rework en procedure_instances (14-tramites-rework-core.sql).
    /// </remarks>
    public partial class SyncPendingModelCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  ADD COLUMN IF NOT EXISTS modalidad_entrada varchar(20) NOT NULL DEFAULT 'matricula_inicial',
                  ADD COLUMN IF NOT EXISTS tipologia_codigo varchar(40),
                  ADD COLUMN IF NOT EXISTS checklist_estado jsonb NOT NULL DEFAULT '{}';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE tramites.procedure_instances
                  DROP COLUMN IF EXISTS checklist_estado,
                  DROP COLUMN IF EXISTS tipologia_codigo,
                  DROP COLUMN IF EXISTS modalidad_entrada;
                """);
        }
    }
}
