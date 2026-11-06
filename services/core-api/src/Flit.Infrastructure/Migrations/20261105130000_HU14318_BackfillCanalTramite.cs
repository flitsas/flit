using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14318 (Feature #14315, Épica #14296) — backfill de <c>channel</c> y <c>channel_transit_office_id</c> en
/// los trámites de la integración ICT y del portal Persona Natural ya creados. DDL:
/// <c>170-HU14318-backfill-canal.sql</c>. Sin Designer: sin los atributos la migración no se descubre.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261105130000_HU14318_BackfillCanalTramite")]
public partial class HU14318_BackfillCanalTramite : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("170-HU14318-backfill-canal.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intencionalmente vacío: el backfill no se revierte. Una fila con canal asignado aquí no se
        // distingue de una creada ya con canal, y borrarlo dejaría a los trámites del portal e ICT
        // leyéndose como «tramites». Quitar las columnas lo hace la migración HU14317.
    }
}
