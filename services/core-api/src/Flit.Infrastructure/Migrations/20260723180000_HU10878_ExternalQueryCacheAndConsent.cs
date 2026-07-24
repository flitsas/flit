using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10878 (Feature #10862, CF-04) — ADR-0030 (caché cross-trámite de consultas externas con
    /// TTL configurable por fuente) + ADR-0031 (gate mínimo de consentimiento Habeas Data para el
    /// reúso de datos de persona). El DDL embebido (40-HU10878-external-query-cache.sql):
    /// 1) agrega <c>cache_ttl_hours</c> (GLOBAL por fuente, sin tenant_id — excepción A20/ADR-0019
    ///    ya aplicada a este catálogo) a <c>tramites.external_data_sources</c> con seed idempotente;
    /// 2) crea <c>tramites.external_query_cache</c> (tenant-scoped, RLS, índices únicos parciales
    ///    persona/vehículo, triggers row_version+audit, sin soft-delete — excepción A6 documentada,
    ///    mismo criterio que admin.signature_vault);
    /// 3) crea <c>tramites.person_data_consents</c> (tenant-scoped, RLS, triggers row_version+audit,
    ///    fail-safe: sin fila o status != 'granted' = no reúso, nunca bloquea el trámite).
    /// Ambas tablas nuevas quedarán mapeadas por Flit.Infrastructure con <c>ExcludeFromMigrations()</c>
    /// (mismo patrón que admin.signature_vault, HU10642: RLS/triggers/CHECKs que el MigrationBuilder
    /// no modela) cuando el backend-agent agregue las entidades + <c>IEntityTypeConfiguration</c>
    /// (Flit.Infrastructure/Persistence/Configurations/Tramites/ExternalQueryCacheEntryConfiguration.cs,
    /// PersonDataConsentConfiguration.cs) — el <c>FlitDbContextModelSnapshot.cs</c> se regenera
    /// automáticamente con <c>dotnet ef migrations add</c> en ese momento (no se edita a mano aquí,
    /// ver nota del ADR "NO editar a mano"; esta migración es descubrible al arrancar por los
    /// atributos <c>[DbContext]</c>/<c>[Migration]</c> inline, sin depender del snapshot — mismo
    /// patrón que HU10879_CurrentStep / HU10881_PrendaDocumentRequirementSeed).
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260723180000_HU10878_ExternalQueryCacheAndConsent")]
    public partial class HU10878_ExternalQueryCacheAndConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("40-HU10878-external-query-cache.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS tramites.person_data_consents CASCADE;
                DROP TABLE IF EXISTS tramites.external_query_cache CASCADE;

                ALTER TABLE tramites.external_data_sources
                  DROP COLUMN IF EXISTS cache_ttl_hours;
                """);
        }
    }
}
