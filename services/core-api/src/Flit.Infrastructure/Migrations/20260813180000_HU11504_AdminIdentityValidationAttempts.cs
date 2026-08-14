using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11504 — agrega el conteo de intentos (<c>attempts</c>/<c>max_attempts</c>/<c>last_attempt_at</c>)
/// a <c>admin.admin_identity_validations</c>, contraparte preventiva del Bug #11503 para el camino admin.
/// Mismo patrón que <c>HU10907_AdminIdentityValidations</c>: la entidad está ExcludeFromMigrations (el DDL
/// crudo lleva el esquema), así que el cuerpo lo reemplaza el LoadUp del DDL embebido
/// (74-HU11504-admin-identity-validations-attempts.sql).
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260813180000_HU11504_AdminIdentityValidationAttempts")]
public partial class HU11504_AdminIdentityValidationAttempts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("74-HU11504-admin-identity-validations-attempts.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE admin.admin_identity_validations " +
            "DROP COLUMN IF EXISTS attempts, " +
            "DROP COLUMN IF EXISTS max_attempts, " +
            "DROP COLUMN IF EXISTS last_attempt_at;");
    }
}
