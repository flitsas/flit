using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14301 (Feature #14300, Épica #14271) — carta de identidad de FLIT para la validación manual aprobada: columnas
/// <c>letter_*</c> en <c>tramites.procedure_instance_biometric_validations</c>. DDL: <c>169-HU14301-carta-identidad-manual.sql</c>.
/// Lleva los atributos inline (sin Designer): sin ellos la migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261105120000_HU14301_CartaIdentidadManual")]
public partial class HU14301_CartaIdentidadManual : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("169-HU14301-carta-identidad-manual.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            ALTER TABLE tramites.procedure_instance_biometric_validations
                DROP COLUMN IF EXISTS letter_generated_at,
                DROP COLUMN IF EXISTS letter_path,
                DROP COLUMN IF EXISTS letter_pdf_sha256,
                DROP COLUMN IF EXISTS letter_sha256,
                DROP COLUMN IF EXISTS letter_series;
            """);
}
