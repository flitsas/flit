using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10907 (ADR-0034) — bloque de validación de identidad ADMINISTRATIVA desacoplada de un
    /// trámite. El DDL embebido (40-HU10907-admin-identity-validations.sql) crea la tabla
    /// <c>admin.admin_identity_validations</c> (agnóstica del sujeto: anclada por subject_type +
    /// subject_ref) con RLS por tenant (app.current_tenant_id), CHECK de estado, índices (sujeto,
    /// verification_id, vigencia) y triggers (row_version + audit) que el MigrationBuilder no modela. La
    /// entidad está ExcludeFromMigrations (mismo patrón que HU10642_SignatureVault / HU10900): el DDL
    /// crudo lleva el esquema y el snapshot solo refleja el modelo EF, por lo que
    /// <c>ef migrations add</c> generó un cuerpo vacío que aquí se reemplaza por el LoadUp del DDL.
    /// </remarks>
    public partial class HU10907_AdminIdentityValidations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("40-HU10907-admin-identity-validations.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("DROP TABLE IF EXISTS admin.admin_identity_validations CASCADE;");
    }
}
