using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Configurador GLOBAL de rondas de validación de identidad: <c>admin.identity_rounds_config</c> (una sola fila) y
/// <c>admin.identity_rounds_alert_audit</c> (idempotencia y auditoría de las alertas). DDL:
/// <c>205-identity-rounds-config-enforced.sql</c>. Lleva los atributos inline (sin Designer): sin ellos EF no la descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261124100000_IdentityRoundsConfig")]
public partial class IdentityRoundsConfig : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("205-identity-rounds-config-enforced.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS admin.identity_rounds_alert_audit;
            DROP TABLE IF EXISTS admin.identity_rounds_config;
            """);
}
