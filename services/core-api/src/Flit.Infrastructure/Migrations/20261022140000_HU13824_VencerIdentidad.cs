using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #13824 (Feature #13820, Épica #13817) — «Vencer» una identidad aprobada: columnas <c>expired_manually_at</c> y
/// <c>valid_until_previous</c> en <c>tramites.procedure_instance_biometric_validations</c>, módulo RBAC
/// <c>validaciones-identidad-admin</c> y permiso <c>identidad.validaciones.vencer</c> (solo SuperAdmin).
/// DDL: <c>151-HU13824-vencer-identidad-enforced.sql</c>. Lleva los atributos inline (sin Designer): sin ellos la
/// migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261022140000_HU13824_VencerIdentidad")]
public partial class HU13824_VencerIdentidad : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("151-HU13824-vencer-identidad-enforced.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Retira el permiso y el módulo y las columnas. Las identidades vencidas a mano conservan su <c>valid_until</c> (ya
    /// vencido): el vencimiento no se revierte.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM security.role_permissions rp USING security.permissions p
             WHERE rp.permission_id = p.id AND p.slug = 'identidad.validaciones.vencer';
            DELETE FROM security.permissions WHERE slug = 'identidad.validaciones.vencer';
            DELETE FROM security.modules WHERE code = 'validaciones-identidad-admin';
            DROP INDEX IF EXISTS tramites.ix_biometric_validations_expired_manually;
            ALTER TABLE tramites.procedure_instance_biometric_validations
                DROP COLUMN IF EXISTS valid_until_previous,
                DROP COLUMN IF EXISTS expired_manually_at;
            """);
}
