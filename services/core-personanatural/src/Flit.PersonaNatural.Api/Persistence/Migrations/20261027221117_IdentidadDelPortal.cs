using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Flit.PersonaNatural.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentidadDelPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "reenvios",
                schema: "personanatural",
                table: "persona_solicitante",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ultimo_reenvio_en",
                schema: "personanatural",
                table: "persona_solicitante",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "auditoria_identidad",
                schema: "personanatural",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    solicitud_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parte = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ordinal = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    desde = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    hasta = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    origen = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ocurrio_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auditoria_identidad", x => x.id);
                    table.ForeignKey(
                        name: "fk_auditoria_identidad_solicitud",
                        column: x => x.solicitud_id,
                        principalSchema: "personanatural",
                        principalTable: "solicitud_tramite",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_identidad_solicitud_fecha",
                schema: "personanatural",
                table: "auditoria_identidad",
                columns: new[] { "solicitud_id", "ocurrio_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auditoria_identidad",
                schema: "personanatural");

            migrationBuilder.DropColumn(
                name: "reenvios",
                schema: "personanatural",
                table: "persona_solicitante");

            migrationBuilder.DropColumn(
                name: "ultimo_reenvio_en",
                schema: "personanatural",
                table: "persona_solicitante");
        }
    }
}
