using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU10542_ActorPersonTypeAndRepLegal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "es_representante_legal",
                schema: "tramites",
                table: "procedure_instance_actors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "person_type",
                schema: "tramites",
                table: "procedure_instance_actors",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "es_representante_legal",
                schema: "tramites",
                table: "procedure_instance_actors");

            migrationBuilder.DropColumn(
                name: "person_type",
                schema: "tramites",
                table: "procedure_instance_actors");
        }
    }
}
