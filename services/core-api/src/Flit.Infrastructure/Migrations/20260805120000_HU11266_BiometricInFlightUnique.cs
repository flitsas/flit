using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11266 (D12) — índice único parcial sobre documento normalizado filtrado a estados en vuelo.
/// Cierra la carrera de dos POST simultáneos de inicio de validación de identidad.
/// Atributos inline [DbContext]/[Migration] (sin .Designer.cs) — mismo patrón que HU10943.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260805120000_HU11266_BiometricInFlightUnique")]
public partial class HU11266_BiometricInFlightUnique : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("56-HU11266-biometric-inflight-unique.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP INDEX IF EXISTS tramites.uq_biometric_validations_inflight_doc_norm;");
    }
}
