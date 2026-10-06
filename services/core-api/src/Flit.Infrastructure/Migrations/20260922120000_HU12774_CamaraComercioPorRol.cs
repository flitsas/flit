using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12774 — tipos documentales del certificado de Cámara de Comercio, uno por rol
/// (<c>vendedor</c>, <c>comprador</c>, <c>locatario</c>). Acredita quién representa a la sociedad,
/// así que se pide a cada actor persona jurídica y se carga en el paso de ese actor.
/// Un código por rol para que el certificado de una parte no deje satisfecha a la otra.
/// DDL: <c>118-HU12774-camara-comercio-por-rol.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260922120000_HU12774_CamaraComercioPorRol")]
public partial class HU12774_CamaraComercioPorRol : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("118-HU12774-camara-comercio-por-rol.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM tramites.document_types
             WHERE code IN (
                 'camara_comercio_vendedor',
                 'camara_comercio_comprador',
                 'camara_comercio_locatario');
            """);
}
