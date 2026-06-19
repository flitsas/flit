using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10201 — Cableado de proveedores de consulta (data-only) | Feature #10116.
    /// AC1/AC2: cablea RUNT_VEHICLE al proveedor Verifik (external_refs real).
    /// AC3: crea la fuente externa stub FLIT_INTEGRATIONS y el template FLIT_GATEWAY_DEMO.
    /// Solo datos (catálogos globales): no modifica el modelo, el snapshot no cambia.
    /// Idempotente: el .sql usa UPDATE + ON CONFLICT DO NOTHING.
    /// </remarks>
    public partial class HU10201_ConsultationProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("13-HU10201-consultation-providers.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revierte la configuración del proveedor en RUNT_VEHICLE y elimina el stub.
            // Orden FK: el template (FLIT_GATEWAY_DEMO) antes que su fuente (FLIT_INTEGRATIONS).
            migrationBuilder.Sql(@"
UPDATE tramites.consultation_templates
  SET external_refs = '{}'::jsonb, updated_at = now()
  WHERE code = 'RUNT_VEHICLE';
DELETE FROM tramites.consultation_templates WHERE code = 'FLIT_GATEWAY_DEMO';
DELETE FROM tramites.external_data_sources WHERE code = 'FLIT_INTEGRATIONS';
");
        }
    }
}
