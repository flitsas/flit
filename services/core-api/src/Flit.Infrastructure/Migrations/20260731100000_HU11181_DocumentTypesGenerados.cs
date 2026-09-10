using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #11181 (Feature #11174) — catálogo de documentos generados. El DDL embebido
/// (46-HU11181-document-types-generados.sql) agrega
/// <c>tramites.document_types.is_system_generated</c> y <c>generated_sort_order</c>, da de alta
/// los seis tipos generados que faltaban y marca los doce que produce el sistema.
/// <para>
/// La entidad <c>DocumentType</c> está <c>ExcludeFromMigrations</c> (el esquema lo lleva el DDL
/// crudo), por lo que el scaffolding no emite <c>AddColumn</c>: el <c>Up</c> aplica la DDL y el
/// snapshot refleja las propiedades nuevas del modelo.
/// </para>
/// <para>
/// No se toca <c>procedure_document_requirements</c>: el checklist del gestor y la obligatoriedad
/// quedan exactamente igual (AC3/AC4).
/// </para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260731100000_HU11181_DocumentTypesGenerados")]
public partial class HU11181_DocumentTypesGenerados : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("46-HU11181-document-types-generados.sql"));
    }

    /// <inheritdoc />
    /// <remarks>
    /// AC5 — reversible sin registros huérfanos: primero se borra la prelación OT que apunte a los
    /// tipos dados de alta aquí (FK <c>ot_document_precedence.document_type_id</c> es RESTRICT),
    /// luego los tipos —solo si nadie más los referencia— y por último las columnas. Los tipos que
    /// ya existían antes de la HU (fur, compraventa, …) no se borran: solo pierden la marca al caer
    /// la columna.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DELETE FROM admin.ot_document_precedence p
            USING tramites.document_types dt
            WHERE p.document_type_id = dt.id
              AND dt.code IN ('certificado_identidad', 'certificado_identidad_vendedor',
                              'certificado_rues', 'certificado_rnmc', 'escritura', 'escritura_comprador');

            DELETE FROM tramites.document_types dt
            WHERE dt.code IN ('certificado_identidad', 'certificado_identidad_vendedor',
                              'certificado_rues', 'certificado_rnmc', 'escritura', 'escritura_comprador')
              AND NOT EXISTS (
                    SELECT 1 FROM tramites.procedure_document_requirements r
                    WHERE r.document_type_id = dt.id)
              AND NOT EXISTS (
                    SELECT 1 FROM tramites.document_order_overrides o
                    WHERE o.document_type_id = dt.id);

            ALTER TABLE tramites.document_types DROP COLUMN IF EXISTS generated_sort_order;
            ALTER TABLE tramites.document_types DROP COLUMN IF EXISTS is_system_generated;
            """);
}
