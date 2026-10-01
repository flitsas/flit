using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13177 (Feature #13119, Épica #13090) — tabla <c>admin.mandate_signer_associated_companies</c>: a qué
/// compañías de FLIT (por tenant) se asocia un mandatario en un organismo (nivel 3 de la prelación, ADR-0066).
/// DDL embebido e idempotente: <c>126-HU13177-mandatario-companias-asociadas.sql</c>. La reversa elimina la
/// tabla (nace vacía; no hay datos previos que conservar).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260930190000_HU13177_MandatarioCompaniasAsociadas")]
public partial class HU13177_MandatarioCompaniasAsociadas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("126-HU13177-mandatario-companias-asociadas.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS admin.mandate_signer_associated_companies;");
}
