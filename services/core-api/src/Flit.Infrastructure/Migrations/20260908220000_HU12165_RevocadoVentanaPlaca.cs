using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12165 (Feature #12156, revocación OT + ventana de placa) — agrega
/// <c>plate_assigned_at</c>/<c>plate_updated_at</c> a <c>tramites.procedure_instances</c> y
/// <c>is_historico</c> a <c>tramites.procedure_instance_attachments</c>. El estado de negocio
/// <c>revocado</c> (TramiteEstado.Revocado) no requiere DDL: <c>status</c> no tiene CHECK de enum en
/// BD. DDL: <c>105-HU12165-revocado-ventana-placa.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260908220000_HU12165_RevocadoVentanaPlaca")]
public partial class HU12165_RevocadoVentanaPlaca : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("105-HU12165-revocado-ventana-placa.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Aditiva y reversible sin condiciones: las tres columnas son nuevas y nunca tuvieron backfill,
    /// así que eliminarlas no pierde ningún dato que existiera antes de esta migración.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            ALTER TABLE tramites.procedure_instance_attachments
                DROP COLUMN IF EXISTS is_historico;

            ALTER TABLE tramites.procedure_instances
                DROP COLUMN IF EXISTS plate_updated_at,
                DROP COLUMN IF EXISTS plate_assigned_at;
            """);
}
