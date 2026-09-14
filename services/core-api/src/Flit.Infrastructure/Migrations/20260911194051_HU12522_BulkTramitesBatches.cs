using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HU12522_BulkTramitesBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bulk_tramites_batches",
                schema: "tramites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    rows_with_structural_errors = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bulk_tramites_batches", x => x.id);
                    table.CheckConstraint("ck_bulk_tramites_batches_total_rows", "total_rows BETWEEN 1 AND 50");
                });

            migrationBuilder.CreateTable(
                name: "bulk_tramites_batch_rows",
                schema: "tramites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuidv7()"),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_number = table.Column<int>(type: "integer", nullable: false),
                    values = table.Column<string>(type: "jsonb", nullable: false),
                    structural_error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    outcome_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    procedure_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bulk_tramites_batch_rows", x => x.id);
                    table.ForeignKey(
                        name: "fk_bulk_tramites_batch_rows_bulk_tramites_batches_batch_id",
                        column: x => x.batch_id,
                        principalSchema: "tramites",
                        principalTable: "bulk_tramites_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bulk_tramites_batch_rows_pending",
                schema: "tramites",
                table: "bulk_tramites_batch_rows",
                column: "batch_id",
                filter: "structural_error_code IS NULL AND outcome IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_bulk_tramites_batch_rows_batch_row",
                schema: "tramites",
                table: "bulk_tramites_batch_rows",
                columns: new[] { "batch_id", "row_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bulk_tramites_batches_tenant_created",
                schema: "tramites",
                table: "bulk_tramites_batches",
                columns: new[] { "tenant_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bulk_tramites_batch_rows",
                schema: "tramites");

            migrationBuilder.DropTable(
                name: "bulk_tramites_batches",
                schema: "tramites");
        }
    }
}
