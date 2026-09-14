using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Epic #12543 — aceptación de Términos y Condiciones por cada creación de trámite. DDL en
/// <c>113-E12543-procedure-terms-acceptances.sql</c>, que es la fuente de verdad; esta migración
/// solo lo ejecuta.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260914200000_E12543_ProcedureTermsAcceptances")]
public partial class E12543_ProcedureTermsAcceptances : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("113-E12543-procedure-terms-acceptances.sql"));

    /// <inheritdoc />
    /// <remarks>Reversible: la tabla es nueva y nada más la referencia.</remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS tr_procedure_terms_acceptances_audit ON tramites.procedure_terms_acceptances;
            DROP TABLE IF EXISTS tramites.procedure_terms_acceptances;
            """);
}
