using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Flit.Consultas.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AvisosKyverum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "avisos",
                schema: "consultas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    proveedor = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    referencia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resultado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cuerpo = table.Column<string>(type: "text", nullable: false),
                    recibido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avisos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                schema: "consultas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "validaciones_kyverum",
                schema: "consultas",
                columns: table => new
                {
                    validacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    producto = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    verification_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    secreto_cifrado = table.Column<string>(type: "text", nullable: true),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_validaciones_kyverum", x => x.validacion_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_avisos_referencia",
                schema: "consultas",
                table: "avisos",
                columns: new[] { "referencia_id", "recibido_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avisos",
                schema: "consultas");

            migrationBuilder.DropTable(
                name: "data_protection_keys",
                schema: "consultas");

            migrationBuilder.DropTable(
                name: "validaciones_kyverum",
                schema: "consultas");
        }
    }
}
