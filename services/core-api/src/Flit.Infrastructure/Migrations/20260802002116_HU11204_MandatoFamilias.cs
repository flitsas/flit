using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #11204 (Feature #11191) — familias de mandatario y datos por organismo. El DDL embebido
    /// (<c>50-HU11204-mandato-familias.sql</c>) retira el CHECK cerrado de <c>template_code</c>, agrega
    /// <c>mandatary_family</c> (con su propio CHECK), <c>chamber_city</c> y <c>mandatary_sigla</c>, y hace
    /// backfill de lo que hasta ahora estaba incrustado en el generador.
    ///
    /// <para>La entidad está <c>ExcludeFromMigrations</c>, así que el scaffolding no emite
    /// <c>AddColumn</c>: el esquema lo lleva el DDL crudo y aquí solo se ejecuta.</para>
    /// </summary>
    public partial class HU11204_MandatoFamilias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("50-HU11204-mandato-familias.sql"));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Reversible: se retiran las tres columnas nuevas. NO se restaura el CHECK cerrado de
        /// <c>template_code</c>: si entretanto se configuró un organismo nuevo reutilizando una redacción,
        /// volver a cerrarlo dejaría la tabla en un estado que la propia restricción rechaza.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE admin.transit_office_mandate_config
                  DROP CONSTRAINT IF EXISTS ck_transit_office_mandate_config_family,
                  DROP COLUMN IF EXISTS mandatary_family,
                  DROP COLUMN IF EXISTS chamber_city,
                  DROP COLUMN IF EXISTS mandatary_sigla;
                """);
        }
    }
}
