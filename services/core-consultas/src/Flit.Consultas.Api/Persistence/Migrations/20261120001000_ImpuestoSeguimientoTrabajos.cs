using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Consultas.Api.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Seguimiento TrabajoId → id externo de impuesto asíncrono (diseño §2.2, D4). Tabla técnica efímera sin PII.
    /// Excepciones checklist: sin FK a tenants ni RLS (schema propio, ADR-0064); sin row_version/audit_log/soft delete.
    /// </remarks>
    [DbContext(typeof(ConsultasDb))]
    [Migration("20261120001000_ImpuestoSeguimientoTrabajos")]
    public partial class ImpuestoSeguimientoTrabajos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vehicle_tax_jobs",
                schema: "consultas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trabajo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    external_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    poll_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_tax_jobs", x => x.id);
                    table.CheckConstraint("ck_vehicle_tax_jobs_status",
                        "status IN ('reserved','queued','running','succeeded','partial','failed','timeout','error')");
                },
                comment: "Seguimiento TrabajoId del bus -> id de la consulta asíncrona de impuesto (D4). Sin placa, documento ni respuesta. Retención 24 h.");

            migrationBuilder.CreateIndex(
                name: "uq_vehicle_tax_jobs_trabajo_id",
                schema: "consultas",
                table: "vehicle_tax_jobs",
                column: "trabajo_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_tax_jobs_tenant_id_created_at",
                schema: "consultas",
                table: "vehicle_tax_jobs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_tax_jobs_created_at",
                schema: "consultas",
                table: "vehicle_tax_jobs",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vehicle_tax_jobs",
                schema: "consultas");
        }
    }
}
