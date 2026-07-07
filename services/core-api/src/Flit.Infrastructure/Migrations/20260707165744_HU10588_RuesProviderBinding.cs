using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10588 — Binding del proveedor RUES a la plantilla RUES_ACTOR_JURIDICAL | Feature #10583 (2ª ola).
    /// Cablea external_refs = {"provider":"verifik_rues","endpointKey":"rues_juridical"} para que el
    /// registry resuelva <c>VerifikRuesConsultationProvider</c> (mock por defecto). Solo datos
    /// (catálogo global): no modifica el modelo, el snapshot no cambia. Idempotente (UPDATE por code).
    /// </remarks>
    public partial class HU10588_RuesProviderBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("23-HU10588-rues-provider-binding.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revierte el binding del proveedor en RUES_ACTOR_JURIDICAL al estado del seed (external_refs vacío).
            migrationBuilder.Sql(@"
UPDATE tramites.consultation_templates
  SET external_refs = '{}'::jsonb, updated_at = now()
  WHERE code = 'RUES_ACTOR_JURIDICAL';
");
        }
    }
}
