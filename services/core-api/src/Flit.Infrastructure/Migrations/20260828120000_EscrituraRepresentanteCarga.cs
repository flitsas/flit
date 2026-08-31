using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Tipos documentales de la escritura del representante legal cargada por el gestor, para el caso en
/// que el representante capturado no está en el módulo de representantes de la compañía y por tanto
/// no tiene escritura que el sistema pueda apalancar.
/// DDL: <c>97-escritura-representante-carga.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260828120000_EscrituraRepresentanteCarga")]
public partial class EscrituraRepresentanteCarga : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("97-escritura-representante-carga.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM tramites.document_types
             WHERE code IN (
                 'escritura_representante',
                 'escritura_representante_vendedor',
                 'escritura_representante_locatario');
            """);
}
