using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #10930 (Feature #10929, RL-flujo-ajustes) — baúl de firmas: deprecar NIT y agregar código
    /// hash. La firma es exclusivamente de la PERSONA y del tenant. El DDL embebido
    /// (41-HU10930-signature-vault-persona.sql) agrega <c>codigo_hash</c>, vuelve <c>nit_empresa</c>
    /// nullable y recrea el índice único <c>uq_signature_vault_activa</c> sobre (tenant, documento)
    /// —el NIT sale de la llave—, más el índice de consumo por (tenant, documento, estado); operaciones
    /// que el MigrationBuilder no modela (la entidad está ExcludeFromMigrations, igual que HU10642).
    /// </remarks>
    public partial class HU10930_SignatureVaultPersona : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("41-HU10930-signature-vault-persona.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverso best-effort: se restablece la unicidad e índice previos por (tenant, NIT, documento)
            // y se retira codigo_hash. nit_empresa se deja nullable (revertir a NOT NULL podría fallar si
            // ya hay filas sin NIT); es un reverso seguro y no destructivo del NIT.
            migrationBuilder.Sql("DROP INDEX IF EXISTS admin.ix_signature_vault_tenant_document_estado;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS admin.uq_signature_vault_activa;");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS uq_signature_vault_activa " +
                "ON admin.signature_vault(tenant_id, nit_empresa, document_number) " +
                "WHERE estado = 'activa';");
            migrationBuilder.Sql("ALTER TABLE admin.signature_vault DROP COLUMN IF EXISTS codigo_hash;");
        }
    }
}
