using System;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10590 (DEV-ONLY) — Seed del procedure_type TRASPASO_UNILATERAL (leasing). Mirror del seed
    /// TRASPASO_STANDARD (Slice 4b): publica el code TRASPASO_UNILATERAL (family TRASPASO, compartida con
    /// el estándar) con config mínima (step→section→fields placa-first: plate, owner_document_type,
    /// owner_document_number). Al crear una instancia por modalidad 'traspaso_unilateral' el handler
    /// resuelve este code por <c>TipologiaResolver.FromType</c> y deriva modalidad_entrada +
    /// tipologia_codigo = 'traspaso_unilateral' SIN colapsar a traspaso_standard.
    /// Idempotente: el .sql usa ON CONFLICT DO NOTHING / WHERE NOT EXISTS.
    ///
    /// Migración HAND-AUTHORED con atributos inline [DbContext]/[Migration] y sin Designer (patrón N03 /
    /// HU #10536): las tablas de <c>tramites</c> están ExcludeFromMigrations (DDL por SQL crudo), por lo
    /// que el diff EF queda vacío; sin los atributos inline EF NO descubre la migración.
    ///
    /// GATE POR ENTORNO (patrón TramitesTraspasoDevSeed): la migración SIEMPRE queda registrada en
    /// __EFMigrationsHistory (para no desincronizar el historial entre entornos), pero el SQL del seed
    /// SOLO se emite cuando ASPNETCORE_ENVIRONMENT == "Development" o FLIT_DEV_SEED activo. En prod el
    /// Up() es un no-op: NO publica ni siembra nada.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260707220000_HU10590_TraspasoUnilateralSeed")]
    public partial class HU10590_TraspasoUnilateralSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!IsDevSeedEnabled())
            {
                return;
            }

            migrationBuilder.Sql(EmbeddedDdl.LoadUp("25-tramites-traspaso-unilateral-dev-seed.sql"));
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
            // DEV-ONLY seed: revierte publicación y elimina el tipo de traspaso unilateral de demo.
            migrationBuilder.Sql(@"
DELETE FROM tramites.procedure_steps s
  USING tramites.procedure_types pt
  WHERE s.procedure_type_id = pt.id AND pt.code = 'TRASPASO_UNILATERAL' AND s.code = 'CONSULTA_PLACA';
DELETE FROM tramites.procedure_types WHERE code = 'TRASPASO_UNILATERAL';
");
        }
    }
}
