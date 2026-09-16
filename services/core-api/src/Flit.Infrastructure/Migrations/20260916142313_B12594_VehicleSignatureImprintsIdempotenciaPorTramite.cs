using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// Bug #12594 — la firma de impronta manual bloqueaba el mismo PDF base usado en dos trámites
    /// distintos porque el índice único parcial era global por <c>document_hash</c>. Se reemplaza por
    /// <c>(procedure_instance_id, document_hash) WHERE deleted_at IS NULL</c>. DDL en
    /// <c>115-B12594-vehicle-signature-imprints-idempotencia-por-tramite.sql</c>, que es la fuente
    /// de verdad; esta migración solo lo ejecuta. Sin datos.
    /// </summary>
    /// <remarks>
    /// Los atributos <c>[DbContext]</c> y <c>[Migration]</c> viven en el <c>.Designer.cs</c> generado
    /// por <c>dotnet ef migrations add</c> (clase parcial).
    /// </remarks>
    public partial class B12594_VehicleSignatureImprintsIdempotenciaPorTramite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("115-B12594-vehicle-signature-imprints-idempotencia-por-tramite.sql"));

        /// <inheritdoc />
        /// <remarks>
        /// Reversible: restaura el índice único global de <c>99-…-partial-unique-hash.sql</c>.
        /// Falla (23505) si ya existen filas activas con el mismo hash en trámites distintos: es el
        /// estado que el Bug #12594 habilita y no se elimina información al revertir.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS tramites.uq_vehicle_signature_imprints_instance_document_hash_active;

                CREATE UNIQUE INDEX IF NOT EXISTS uq_vehicle_signature_imprints_document_hash_active
                  ON tramites.vehicle_signature_imprints (document_hash)
                  WHERE deleted_at IS NULL;

                COMMENT ON COLUMN tramites.vehicle_signature_imprints.document_hash IS
                  'SHA-256 hex del PDF de impronta antes de estampar (idempotencia entre filas activas).';
                """);
    }
}
