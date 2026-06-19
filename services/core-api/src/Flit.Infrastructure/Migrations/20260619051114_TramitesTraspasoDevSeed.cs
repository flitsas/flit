using System;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Slice 4b (DEV-ONLY) — Seed de un procedure_type de TRASPASO para demo de la modalidad
    /// placa-first. Mirror del seed HU #10200 (MATRICULA_NUEVA): publica TRASPASO_STANDARD
    /// (familia TRASPASO) con config mínima (step→section→fields: plate, owner_document_type,
    /// owner_document_number). Al crear una instancia de este tipo, el handler deriva
    /// modalidad_entrada='traspaso' y tipologia_codigo='traspaso_standard' (Slice 4b), activando
    /// el gating de documentos de traspaso en runtime.
    /// Idempotente: el .sql usa ON CONFLICT DO NOTHING / WHERE NOT EXISTS.
    ///
    /// GATE POR ENTORNO (mismo patrón D-200-1): la migración SIEMPRE queda registrada en
    /// __EFMigrationsHistory (para no desincronizar el historial entre entornos), pero el SQL
    /// del seed SOLO se emite cuando ASPNETCORE_ENVIRONMENT == "Development" o FLIT_DEV_SEED
    /// activo. En prod el Up() es un no-op: NO publica ni siembra nada.
    /// </remarks>
    public partial class TramitesTraspasoDevSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!IsDevSeedEnabled())
            {
                return;
            }

            migrationBuilder.Sql(EmbeddedDdl.LoadUp("15-tramites-traspaso-dev-seed.sql"));
        }

        private static bool IsDevSeedEnabled()
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var devSeedFlag = Environment.GetEnvironmentVariable("FLIT_DEV_SEED");
            return string.Equals(devSeedFlag, "1", StringComparison.Ordinal)
                || string.Equals(devSeedFlag, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DEV-ONLY seed: revierte publicación y elimina el tipo de traspaso de demo.
            migrationBuilder.Sql(@"
DELETE FROM tramites.procedure_steps s
  USING tramites.procedure_types pt
  WHERE s.procedure_type_id = pt.id AND pt.code = 'TRASPASO_STANDARD' AND s.code = 'CONSULTA_PLACA';
DELETE FROM tramites.procedure_types WHERE code = 'TRASPASO_STANDARD';
");
        }
    }
}
