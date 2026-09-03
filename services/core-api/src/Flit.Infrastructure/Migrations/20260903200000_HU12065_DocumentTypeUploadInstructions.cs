using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12065 (Feature #12064) — <c>tramites.document_types.upload_instructions</c>: la instrucción
/// de cargue que lee el gestor en la tarjeta del paso Requisitos, distinta de <c>description</c>
/// (la nota interna del administrador).
/// <para>
/// La entidad <c>DocumentType</c> está <c>ExcludeFromMigrations</c> (el esquema lo lleva el DDL
/// crudo), así que el scaffolding no emite <c>AddColumn</c>: el <c>Up</c> aplica la DDL y el
/// snapshot solo refleja la propiedad nueva del modelo.
/// </para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260903200000_HU12065_DocumentTypeUploadInstructions")]
public partial class HU12065_DocumentTypeUploadInstructions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("102-document-types-upload-instructions.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Reversible sin residuos: al caer la columna se van con ella los textos sembrados. Ningún
    /// otro dato del catálogo se tocó en el <c>Up</c>.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            ALTER TABLE tramites.document_types
                DROP COLUMN IF EXISTS upload_instructions;
            """);
}
