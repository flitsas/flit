using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11183 (Feature #11174) — marca <c>certificado_vigencia_soat_rtm</c> como documento generado
/// para que el OT pueda reordenarlo. Su adjunto se guarda como <c>certificado_soat_rtm</c>: la
/// equivalencia entre ambos códigos vive en <c>ConsolidadoDocumentCodeMap</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260731110000_HU11183_CertificadoSoatRtmGenerado")]
public partial class HU11183_CertificadoSoatRtmGenerado : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("47-HU11183-certificado-soat-rtm-generado.sql"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM admin.ot_document_precedence p
            USING tramites.document_types dt
            WHERE p.document_type_id = dt.id
              AND dt.code = 'certificado_vigencia_soat_rtm';

            UPDATE tramites.document_types
            SET is_system_generated = false,
                generated_sort_order = NULL
            WHERE code = 'certificado_vigencia_soat_rtm';
            """);
}
