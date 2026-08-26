using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Siembra los documentos que acreditan cada causal de <c>CANCELACION_MATRICULA</c>
/// (<c>certificado_dijin</c>, <c>certificado_aseguradora_perito</c>,
/// <c>certificado_autoridad_administrativa</c>) y los ata al trámite como opcionales.
/// DDL: <c>92-cancelacion-causales.sql</c>.
/// <para>La obligatoriedad la pone la causal declarada en el expediente, vía
/// <c>ConditionalDocumentRules.Cancelacion</c>: depende de un dato del trámite, no del tipo.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260826100000_CancelacionCausales")]
public partial class CancelacionCausales : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("92-cancelacion-causales.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Quita los tres requisitos nuevos y deja <c>oficio_judicial</c> como estaba (opcional, orden
    /// 11). Los tipos de documento NO se borran: pueden tener adjuntos apuntándolos y la FK de
    /// <c>procedure_document_requirements</c> es <c>ON DELETE RESTRICT</c> por esa misma razón.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DELETE FROM tramites.procedure_document_requirements r
             USING tramites.procedure_types pt, tramites.document_types dt
             WHERE r.procedure_type_id = pt.id
               AND r.document_type_id = dt.id
               AND pt.code = 'CANCELACION_MATRICULA'
               AND dt.code IN (
                   'certificado_dijin',
                   'certificado_aseguradora_perito',
                   'certificado_autoridad_administrativa');
            """);
}
