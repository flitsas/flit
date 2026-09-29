using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12924 (Épica #12718, ADR-0060) — casos de soporte de DR. FLIT (<c>dr_flit.support_cases</c>) y
/// adjuntos subidos antes de confirmar (<c>dr_flit.support_case_attachments</c>). Sin entidades EF: los
/// repositorios usan SQL directo, igual que el contador de la HU #12919.
/// DDL: <c>120-HU12924-dr-flit-support-cases.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260925110000_HU12924_DrFlitSupportCases")]
public partial class HU12924_DrFlitSupportCases : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("120-HU12924-dr-flit-support-cases.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP TABLE IF EXISTS dr_flit.support_case_attachments;
            DROP TABLE IF EXISTS dr_flit.support_cases;
            """);
}
