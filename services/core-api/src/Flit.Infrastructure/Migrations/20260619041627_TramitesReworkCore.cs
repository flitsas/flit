using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Trámites Rework — Slice 1: schema núcleo (FEAT-2026-06-18-002).
    /// El DDL (14-tramites-rework-core.sql) añade modalidad_entrada/tipologia_codigo/checklist_estado
    /// a procedure_instances y crea attachments, preflight_snapshots, commercial (1:1) y events
    /// con RLS + auditoría. El snapshot/Designer reconcilian el modelo EF (no se emite CreateTable).
    /// Difiere biométrica/firmas/portal a slices 6-7.
    /// </remarks>
    public partial class TramitesReworkCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("14-tramites-rework-core.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Orden FK: tablas hijas antes que las columnas/tabla padre.
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS tramites.procedure_instance_events;
DROP TABLE IF EXISTS tramites.procedure_instance_commercial;
DROP TABLE IF EXISTS tramites.procedure_instance_preflight_snapshots;
DROP TABLE IF EXISTS tramites.procedure_instance_attachments;
ALTER TABLE tramites.procedure_instances
  DROP COLUMN IF EXISTS checklist_estado,
  DROP COLUMN IF EXISTS tipologia_codigo,
  DROP COLUMN IF EXISTS modalidad_entrada;
");
        }
    }
}
