using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Feature #10929 (RL-flujo-ajustes) — Escritura DEL representante. El DDL embebido
    /// (44-HU-representante-escritura.sql) agrega la columna nullable
    /// admin.company_deeds.representative_id (ADD COLUMN IF NOT EXISTS, idempotente), su FK →
    /// admin.company_legal_representatives(id) ON DELETE CASCADE y el índice
    /// (tenant_id, representative_id). La entidad CompanyDeedEntity está ExcludeFromMigrations (el DDL
    /// crudo lleva el esquema), por lo que el scaffolding no emite AddColumn: el Up aplica la DDL y el
    /// snapshot regenerado refleja la nueva propiedad/índice del modelo. Nullable = compat con
    /// escrituras legadas (quedan sin representante y no aparecen en el detalle de ninguno).
    /// </remarks>
    public partial class HU10929_DeedRepresentative : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("44-HU-representante-escritura.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverso best-effort no destructivo (la FK cae con la columna).
            migrationBuilder.Sql(
                "ALTER TABLE admin.company_deeds DROP COLUMN IF EXISTS representative_id;");
        }
    }
}
