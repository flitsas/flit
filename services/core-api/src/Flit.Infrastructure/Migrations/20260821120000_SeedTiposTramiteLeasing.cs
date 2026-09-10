using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Catálogo <c>tramites.procedure_types</c>: matrícula leasing y traspasos de locatario.
/// Idempotente. DDL: <c>78-seed-tipos-tramite-leasing.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260821120000_SeedTiposTramiteLeasing")]
public partial class SeedTiposTramiteLeasing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("78-seed-tipos-tramite-leasing.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DELETE FROM tramites.procedure_types
            WHERE code IN (
                'MATRICULA_LEASING',
                'TRASPASO_UNILATERAL',
                'TRASPASO_TRANSFERENCIA_DE_DOMINIO'
            )
              AND NOT EXISTS (
                  SELECT 1
                  FROM tramites.procedure_instances i
                  WHERE i.procedure_type_id = procedure_types.id
              );
            """);
}
