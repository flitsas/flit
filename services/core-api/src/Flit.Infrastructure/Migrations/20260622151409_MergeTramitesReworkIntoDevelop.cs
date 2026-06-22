using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// Migración de reconciliación del merge del rework de trámites (#10128) con develop.
    ///
    /// <para>NO-OP intencional. Su único propósito es regenerar el <c>FlitDbContextModelSnapshot</c>
    /// como la UNIÓN real de ambos modelos (runtime de trámites + parametrización/snapshot del Admin),
    /// que el auto-merge de git había dejado desincronizado.</para>
    ///
    /// <para>Las tablas de parametrización/Admin (document_types, procedure_document_requirements,
    /// document_order_overrides, document_requirement_overrides, procedure_document_snapshots) las crean
    /// las migraciones SQL (EmbeddedDdl) de develop ya presentes en la cadena; recrearlas aquí en C#
    /// duplicaría DDL y rompería el <c>Migrate()</c> de arranque. Por eso Up/Down quedan vacíos.</para>
    /// </summary>
    public partial class MergeTramitesReworkIntoDevelop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: solo reconcilia el ModelSnapshot (ver resumen de la clase).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op.
        }
    }
}
