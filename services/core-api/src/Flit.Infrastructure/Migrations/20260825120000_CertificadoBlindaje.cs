using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Da código propio al certificado de blindaje (<c>certificado_blindaje</c>) y sustituye con él el
/// genérico <c>otro</c> que el seed 82 dejó como deuda declarada en el requisito de
/// <c>BLINDAJE</c>. DDL: <c>89-certificado-blindaje.sql</c>.
/// <para>Obligatorio en las cuatro opciones del trámite (niveles 1/2/3 y desmonte).</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260825120000_CertificadoBlindaje")]
public partial class CertificadoBlindaje : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("89-certificado-blindaje.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Devuelve el requisito al genérico <c>otro</c>, que es el estado previo. El tipo de documento
    /// NO se borra: podría tener adjuntos ya cargados apuntándolo, y la FK de
    /// <c>procedure_document_requirements</c> es <c>ON DELETE RESTRICT</c> por esa misma razón.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            INSERT INTO tramites.procedure_document_requirements
                (id, procedure_type_id, document_type_id, is_mandatory, default_sort_order)
            SELECT uuidv7(), pt.id, dt.id, true, 10::smallint
              FROM tramites.procedure_types pt
              JOIN tramites.document_types dt ON dt.code = 'otro'
             WHERE pt.code = 'BLINDAJE'
            ON CONFLICT (procedure_type_id, document_type_id) DO NOTHING;

            DELETE FROM tramites.procedure_document_requirements r
             USING tramites.procedure_types pt, tramites.document_types dt
             WHERE r.procedure_type_id = pt.id
               AND r.document_type_id = dt.id
               AND pt.code = 'BLINDAJE'
               AND dt.code = 'certificado_blindaje';
            """);
}
