using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Trámites Rework — Slice 6: biométrica (mock).
    /// El DDL (16-tramites-biometrica.sql) crea procedure_instance_biometric_validations con sus
    /// índices (FK + composite + token_hash único), RLS por tenant y trigger de auditoría. El
    /// snapshot/Designer reconcilian el modelo EF (no se emite CreateTable desde el MigrationBuilder).
    /// El scoring es MOCK determinista (ver IBiometricScorer/MockBiometricScorer en Application).
    /// </remarks>
    public partial class TramitesBiometrica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("16-tramites-biometrica.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TABLE IF EXISTS tramites.procedure_instance_biometric_validations;
");
        }
    }
}
