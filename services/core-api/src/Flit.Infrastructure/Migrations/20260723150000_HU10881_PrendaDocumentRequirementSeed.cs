using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10881 (CF-06) — Asocia el documento de prenda (<c>inscripcion_prenda</c>) a Traspaso
    /// Estándar en <c>tramites.procedure_document_requirements</c> como opcional
    /// (<c>is_mandatory=false</c>, <c>condition_group='prenda'</c>): habilita que el override de
    /// obligatoriedad por OT existente (<c>tramites.document_requirement_overrides</c>, HU #10198)
    /// pueda activarlo para un OT ("Documento de prenda obligatorio") sin exigirlo siempre. Solo
    /// datos (DML puro sobre una tabla EF-mapeada existente, sin cambios de esquema): el diff de
    /// modelo de EF no cambia por esta migración. Se declaran <c>[DbContext]</c>/<c>[Migration]</c>
    /// inline (mismo patrón que HU10879_CurrentStep) para que se descubra y corra al arrancar sin
    /// depender de regenerar el ModelSnapshot completo (que hoy arrastra drift preexistente ajeno a
    /// esta HU, ver <c>section_type</c> en <c>procedure_sections</c>, F08).
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260723150000_HU10881_PrendaDocumentRequirementSeed")]
    public partial class HU10881_PrendaDocumentRequirementSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("39-HU10881-prenda-document-requirement-seed.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM tramites.procedure_document_requirements r
                USING tramites.procedure_types pt, tramites.document_types dt
                WHERE r.procedure_type_id = pt.id
                  AND pt.code = 'TRASPASO_STANDARD'
                  AND r.document_type_id = dt.id
                  AND dt.code = 'inscripcion_prenda';
                """);
        }
    }
}
