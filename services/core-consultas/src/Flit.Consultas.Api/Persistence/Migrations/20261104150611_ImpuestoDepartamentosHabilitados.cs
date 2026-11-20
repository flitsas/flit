using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Consultas.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImpuestoDepartamentosHabilitados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vehicle_tax_departments",
                schema: "consultas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    department_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    external_refs = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_tax_departments", x => x.id);
                },
                comment: "Catálogo global de departamentos con consulta de impuesto. Sin tenant_id: ADR-0080. Lista efectiva: is_active y deleted_at nulo.");

            migrationBuilder.CreateIndex(
                name: "uq_vehicle_tax_departments_department_code",
                schema: "consultas",
                table: "vehicle_tax_departments",
                column: "department_code",
                unique: true);

            // Semilla de public.vehicle_taxes_query_api_departments (2026-11-04). El reloj legado no tenía zona: se guarda como UTC.
            migrationBuilder.Sql(
                """
                INSERT INTO consultas.vehicle_tax_departments
                    (id, department_code, department_name, is_active, created_at, updated_at, deleted_at)
                VALUES
                    ('01941215-0000-7000-8000-000000000017', '17', 'CALDAS',  false, TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', TIMESTAMPTZ '2026-01-15 18:15:19.862895+00'),
                    ('01941215-0000-7000-8000-000000000094', '94', 'GUAINIA', true,  TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', NULL),
                    ('01941215-0000-7000-8000-000000000050', '50', 'META',    true,  TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', NULL),
                    ('01941215-0000-7000-8000-000000000052', '52', 'NARIÑO',  true,  TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', NULL),
                    ('01941215-0000-7000-8000-000000000063', '63', 'QUINDIO', false, TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', TIMESTAMPTZ '2026-01-22 09:02:19.862895+00')
                ON CONFLICT (department_code) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vehicle_tax_departments",
                schema: "consultas");
        }
    }
}
