using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11269 — índice por expresión (tenant + documento Trim+Upper + created_at DESC)
/// para la consulta agrupada DISTINCT ON de la vista por persona (HU #11270).
/// Atributos inline [DbContext]/[Migration] (sin .Designer.cs) — mismo patrón que HU10943.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260805130000_HU11269_BiometricDocNormCreatedIndex")]
public partial class HU11269_BiometricDocNormCreatedIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("57-HU11269-biometric-doc-norm-created.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP INDEX IF EXISTS tramites.ix_biometric_validations_doc_norm_created;");
    }
}
