using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14310 (Feature #14307, Épica #14271) — resultado de la validación de las fotos de la captura manual (6 columnas NULL +
/// CHECK). DDL: <c>171-HU14310-validacion-fotos-captura-manual.sql</c> (169 y 170 reservados para otras ramas).
/// Sin Designer: los atributos son lo que la hace descubrible.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261105120000_HU14310_ValidacionFotosCapturaManual")]
public partial class HU14310_ValidacionFotosCapturaManual : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("171-HU14310-validacion-fotos-captura-manual.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE tramites.procedure_instance_biometric_validations
                DROP CONSTRAINT IF EXISTS ck_biometric_validations_photo_checks;
            ALTER TABLE tramites.procedure_instance_biometric_validations
                DROP COLUMN IF EXISTS face_check_status,
                DROP COLUMN IF EXISTS face_check_reason,
                DROP COLUMN IF EXISTS id_front_check_status,
                DROP COLUMN IF EXISTS id_front_check_reason,
                DROP COLUMN IF EXISTS id_back_check_status,
                DROP COLUMN IF EXISTS id_back_check_reason;
            """);
}
