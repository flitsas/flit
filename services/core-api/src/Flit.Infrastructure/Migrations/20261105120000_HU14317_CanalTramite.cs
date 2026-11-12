using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14317 (Feature #14315, Épica #14296) — columnas <c>channel</c> y <c>channel_transit_office_id</c> en
/// <c>tramites.procedure_instances</c>. DDL: <c>169-HU14317-canal-tramite.sql</c>. Sin backfill (HU #14318).
/// Sin Designer: sin los atributos la migración no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261105120000_HU14317_CanalTramite")]
public partial class HU14317_CanalTramite : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("169-HU14317-canal-tramite.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS tramites.ix_procedure_instances_channel_transit_office_id;
            ALTER TABLE tramites.procedure_instances
                DROP CONSTRAINT IF EXISTS fk_procedure_instances_channel_transit_office,
                DROP CONSTRAINT IF EXISTS ck_procedure_instances_channel_office,
                DROP CONSTRAINT IF EXISTS ck_procedure_instances_channel,
                DROP COLUMN IF EXISTS channel_transit_office_id,
                DROP COLUMN IF EXISTS channel;
            """);
}
