using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Consultas.Api.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #14711 (plan §3.3): deduplicación de los avisos del proveedor de impuesto por (tenant_id, delivery_id) con
    /// <c>INSERT … ON CONFLICT DO NOTHING</c>, purga a 7 días por <c>created_at</c>, y el índice (tenant_id, external_id) de
    /// <c>vehicle_tax_jobs</c> con el que el receptor encuentra la consulta de la empresa. Tabla técnica efímera sin PII ni cuerpo.
    /// Excepciones del checklist, como <c>vehicle_tax_jobs</c>: sin FK a tenants ni RLS (esquema propio, ADR-0064); sin
    /// updated_at/row_version/audit_log/soft delete (filas inmutables que solo se insertan y se purgan).
    /// </remarks>
    public partial class ImpuestoAvisosWebhook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vehicle_tax_webhook_deliveries",
                schema: "consultas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    external_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_tax_webhook_deliveries", x => x.id);
                    table.CheckConstraint("ck_vehicle_tax_webhook_deliveries_attempt", "attempt >= 0");
                },
                comment: "Avisos del proveedor de impuesto ya verificados, para deduplicar por (tenant_id, delivery_id) (HU #14711). Sin cuerpo, firma ni PII. Retención 7 días.");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_tax_jobs_tenant_id_external_id",
                schema: "consultas",
                table: "vehicle_tax_jobs",
                columns: new[] { "tenant_id", "external_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_tax_webhook_deliveries_created_at",
                schema: "consultas",
                table: "vehicle_tax_webhook_deliveries",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "uq_vehicle_tax_webhook_deliveries_tenant_id_delivery_id",
                schema: "consultas",
                table: "vehicle_tax_webhook_deliveries",
                columns: new[] { "tenant_id", "delivery_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vehicle_tax_webhook_deliveries",
                schema: "consultas");

            migrationBuilder.DropIndex(
                name: "ix_vehicle_tax_jobs_tenant_id_external_id",
                schema: "consultas",
                table: "vehicle_tax_jobs");
        }
    }
}
