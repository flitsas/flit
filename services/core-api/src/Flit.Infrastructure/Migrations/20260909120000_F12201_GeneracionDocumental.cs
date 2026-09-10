using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Feature #12201 — Generación documental autónoma, incrementos I1 (Certificado RUES) e I2
    /// (Transferencia de dominio A/B/C). ADR-0056-generacion-documental-standalone (Propuesto).
    /// DDL en <c>Persistence/Sql/Ddl/105-F12201-generacion-documental.sql</c>, que es la fuente de
    /// verdad; esta migración solo lo ejecuta.
    ///
    /// El prefijo es el Feature (F12201) y no una HU porque el trabajo de schema se materializó
    /// antes de registrar las HUs en ADO; precedente de nomenclatura: <c>35-F08-…</c>,
    /// <c>37-F08-…</c>. Al registrar la HU, renombrar ambos archivos NO es posible si esto ya se
    /// aplicó en algún ambiente: la referencia queda en la descripción del PR.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260909120000_F12201_GeneracionDocumental")]
    public partial class F12201_GeneracionDocumental : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("105-F12201-generacion-documental.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversible completo: la tabla es nueva y no se alteró ninguna existente, así que el
            // rollback no puede perder datos ajenos al Feature. Se retiran primero los triggers y
            // la policy (dependen de la tabla) y al final la función, que vive en el schema admin.
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tr_standalone_documents_immutable ON admin.standalone_documents;
                DROP TRIGGER IF EXISTS tr_standalone_documents_audit ON admin.standalone_documents;
                DROP TRIGGER IF EXISTS tr_standalone_documents_row_version ON admin.standalone_documents;
                DROP POLICY IF EXISTS tenant_isolation ON admin.standalone_documents;

                DROP TABLE IF EXISTS admin.standalone_documents;

                DROP FUNCTION IF EXISTS admin.trg_standalone_document_immutable();
                """);
        }
    }
}
