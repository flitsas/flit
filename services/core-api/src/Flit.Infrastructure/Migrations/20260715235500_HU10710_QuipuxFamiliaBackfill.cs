using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Quipux (HU #10710) — rellena <c>external_refs-&gt;'quipux'-&gt;&gt;'familia'</c> en los tipos de
    /// trámite ya sembrados. La familia (MATRICULA | TRASPASO | OTROS) decide cuál de las tres
    /// banderas de la secretaría destino manda (<c>quipux_matricula</c> / <c>_traspaso</c> /
    /// <c>_otros</c>); sin ella el tipo NO es elegible, así que sin este backfill la integración
    /// quedaría inerte allí donde el seed ya había corrido.
    ///
    /// <para>Existe como migración aparte porque <c>33-HU10710-quipux-external-refs-seed.sql</c> ya
    /// está aplicado en los ambientes: editar el DDL de una migración pasada no la re-ejecuta —
    /// EF solo mira <c>__EFMigrationsHistory</c>—, así que el cambio no llegaría a ningún lado. En
    /// una base nueva el seed ya escribe la familia y este backfill queda en no-op.</para>
    ///
    /// <para>No se usa <c>procedure_types.family</c> como origen: esa columna está inconsistente
    /// (conviven <c>VEHICULAR</c>, <c>MATRICULAS</c>, <c>TRASPASO</c> y <c>OTROS</c> por tres seeds
    /// históricos solapados, y <c>VEHICULAR</c> —donde caen <c>TRASPASO</c> y
    /// <c>MATRICULA_INICIAL</c>— ni siquiera existe en el enum <c>ProcedureFamily</c>). Se matchea
    /// por <c>code</c>, igual que el seed.</para>
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260715235500_HU10710_QuipuxFamiliaBackfill")]
    public partial class HU10710_QuipuxFamiliaBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // jsonb_set con create_if_missing = true: añade 'familia' sin tocar el resto del bloque.
            // El guard IS DISTINCT FROM lo hace idempotente y evita disparar en balde los triggers
            // tr_procedure_types_audit / tr_procedure_types_row_version en cada arranque.
            migrationBuilder.Sql(
                """
                UPDATE tramites.procedure_types
                SET external_refs = jsonb_set(external_refs, '{quipux,familia}', '"TRASPASO"', true)
                WHERE external_refs ? 'quipux'
                  AND code IN ('TRASPASO', 'TRASPASO_STANDARD', 'TRASPASO_SIMPLE', 'TRASPASO_LEASING')
                  AND external_refs -> 'quipux' ->> 'familia' IS DISTINCT FROM 'TRASPASO';

                UPDATE tramites.procedure_types
                SET external_refs = jsonb_set(external_refs, '{quipux,familia}', '"MATRICULA"', true)
                WHERE external_refs ? 'quipux'
                  AND code IN ('MATRICULA_INICIAL', 'MATRICULA_NUEVA', 'MATRICULA_REACTIVACION')
                  AND external_refs -> 'quipux' ->> 'familia' IS DISTINCT FROM 'MATRICULA';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE tramites.procedure_types
                SET external_refs = external_refs #- '{quipux,familia}'
                WHERE external_refs -> 'quipux' ? 'familia';
                """);
        }
    }
}
