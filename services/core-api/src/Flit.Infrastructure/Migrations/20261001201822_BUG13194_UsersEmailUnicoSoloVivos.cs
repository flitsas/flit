using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BUG13194_UsersEmailUnicoSoloVivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La DDL de origen (01-HU10146) creó uq_users_email como CONSTRAINT UNIQUE, no como índice
            // suelto: DROP INDEX falla con 2BP01. Se retira como restricción si lo es y como índice si
            // la base se creó desde el modelo EF; ambas ramas son idempotentes.
            migrationBuilder.Sql("ALTER TABLE identity.users DROP CONSTRAINT IF EXISTS uq_users_email;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS identity.uq_users_email;");

            migrationBuilder.CreateIndex(
                name: "uq_users_email",
                schema: "identity",
                table: "users",
                column: "email",
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_users_email",
                schema: "identity",
                table: "users");

            // Se restaura la forma original de la DDL (CONSTRAINT UNIQUE). Falla si ya hay dos cuentas
            // (una eliminada) con el mismo correo: revertir exige depurar esos duplicados antes.
            migrationBuilder.Sql("ALTER TABLE identity.users ADD CONSTRAINT uq_users_email UNIQUE (email);");
        }
    }
}
