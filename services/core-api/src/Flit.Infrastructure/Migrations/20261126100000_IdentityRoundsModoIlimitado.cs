using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Modo sin límite de rondas y umbral de alerta a soporte en <c>admin.identity_rounds_config</c>. DDL:
/// <c>206-identity-rounds-modo-ilimitado.sql</c>. Atributos inline (sin Designer), como la migración anterior de rondas.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261126100000_IdentityRoundsModoIlimitado")]
public partial class IdentityRoundsModoIlimitado : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("206-identity-rounds-modo-ilimitado.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE admin.identity_rounds_config DROP CONSTRAINT IF EXISTS ck_identity_rounds_config_umbral;
            ALTER TABLE admin.identity_rounds_config DROP COLUMN IF EXISTS umbral_alerta_soporte;
            ALTER TABLE admin.identity_rounds_config DROP COLUMN IF EXISTS limite_activo;
            """);
}
