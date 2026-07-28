using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Migración V1→V2 (traspasos) — siembra los tres tipos de documento que solo existían en
    /// Flit V1: <c>limitacion_propiedad</c>, <c>carta_declaratoria</c> y
    /// <c>autorizacion_apoderado</c>.
    ///
    /// V1 arma su expediente en caliente y no lo guarda, así que al migrar un trámite hay que
    /// materializar esas piezas (<c>--tipo transfer-documents</c>); sin su tipo en el catálogo no se
    /// pueden almacenar. El resto del expediente ya tiene tipo en V2 (HU #10520).
    ///
    /// Migración hand-authored: atributos <c>[DbContext]</c> + <c>[Migration]</c> inline y sin
    /// Designer (patrón HU #10536 / N03). Sin estos atributos EF NO descubre la migración.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260727150000_MigracionV1_DocumentTypes")]
    public partial class MigracionV1_DocumentTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("39-migracion-v1-document-types.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Solo se retiran si nadie los usó: un expediente migrado que ya los referencia manda
            // sobre el rollback del catálogo.
            migrationBuilder.Sql(
                """
                DELETE FROM tramites.document_types dt
                 WHERE dt.code IN ('limitacion_propiedad', 'carta_declaratoria', 'autorizacion_apoderado')
                   AND NOT EXISTS (
                       SELECT 1 FROM tramites.procedure_instance_attachments a
                        WHERE a.tipo = dt.code);
                """);
        }
    }
}
