using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FEATURE10707_CommercialValuationTrace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "suggested_source",
                schema: "tramites",
                table: "procedure_instance_commercial",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "suggested_value",
                schema: "tramites",
                table: "procedure_instance_commercial",
                type: "numeric(15,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "value_origin",
                schema: "tramites",
                table: "procedure_instance_commercial",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "avaluo_mock_values",
                schema: "tramites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    match_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    value_cop = table.Column<decimal>(type: "numeric(15,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avaluo_mock_values", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_avaluo_mock",
                schema: "tramites",
                table: "avaluo_mock_values",
                columns: new[] { "match_key", "source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avaluo_mock_values",
                schema: "tramites");

            migrationBuilder.DropColumn(
                name: "suggested_source",
                schema: "tramites",
                table: "procedure_instance_commercial");

            migrationBuilder.DropColumn(
                name: "suggested_value",
                schema: "tramites",
                table: "procedure_instance_commercial");

            migrationBuilder.DropColumn(
                name: "value_origin",
                schema: "tramites",
                table: "procedure_instance_commercial");
        }
    }
}
