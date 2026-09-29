using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12931 (Épica #12718, ADR-0060) — evidencia de la aceptación del tratamiento de datos de DR. FLIT
/// (<c>dr_flit.consent_acceptances</c>). Sin entidad EF: SQL directo como el resto del schema.
/// DDL: <c>121-HU12931-dr-flit-consent-acceptances.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260925130000_HU12931_DrFlitConsentAcceptances")]
public partial class HU12931_DrFlitConsentAcceptances : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("121-HU12931-dr-flit-consent-acceptances.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS dr_flit.consent_acceptances;");
}
