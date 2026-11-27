using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// HU #14711 — amplía <c>ck_tenant_product_credentials_code</c> a los secretos del webhook de <c>diagnostico</c> y suma
    /// <c>attempts</c> / <c>next_attempt_at</c> a <c>platform.tenant_product_provisioning</c>. DDL:
    /// <c>206-alta-credencial-impuesto-diagnostico.sql</c>.
    /// <para>
    /// Generada con <c>dotnet ef migrations add</c>; las dos tablas son <c>ExcludeFromMigrations</c>, así que la herramienta
    /// dejó el cuerpo vacío y solo actualizó el snapshot con las dos columnas. El cuerpo es el DDL.
    /// </para>
    /// </summary>
    public partial class HU14711_AltaCredencialImpuestoDiagnostico : Migration
    {
        /// <summary>
        /// Reversa. Elección documentada: <b>borra solo las filas de los dos secretos del webhook</b> antes de restaurar el CHECK
        /// del DDL 204 (con ellas el CHECK no se podría volver a crear). No se pierde nada irrecuperable: el alta del proveedor es
        /// idempotente y vuelve a entregar el mismo secreto. La <c>impuesto_api_key</c> no se toca.
        /// </summary>
        public const string DownSql = """
            DELETE FROM platform.tenant_product_credentials
             WHERE product_code = 'diagnostico' AND credential_code IN ('impuesto_webhook_secret', 'impuesto_webhook_secret_anterior');
            ALTER TABLE platform.tenant_product_credentials DROP CONSTRAINT IF EXISTS ck_tenant_product_credentials_code;
            ALTER TABLE platform.tenant_product_credentials ADD CONSTRAINT ck_tenant_product_credentials_code
                CHECK ((product_code, credential_code) IN (('diagnostico', 'impuesto_api_key')));
            COMMENT ON TABLE platform.tenant_product_credentials IS
                'ADR-0092 · Credencial de un producto por empresa (v1: diagnostico/impuesto_api_key). Solo core-identity la lee y la escribe. La UI solo ve configurada/no configurada.';
            COMMENT ON COLUMN platform.tenant_product_credentials.credential_code IS NULL;
            DROP INDEX IF EXISTS platform.ix_tenant_product_provisioning_product_code_next_attempt_at;
            ALTER TABLE platform.tenant_product_provisioning DROP CONSTRAINT IF EXISTS ck_tenant_product_provisioning_attempts;
            ALTER TABLE platform.tenant_product_provisioning DROP COLUMN IF EXISTS next_attempt_at, DROP COLUMN IF EXISTS attempts;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("206-alta-credencial-impuesto-diagnostico.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(DownSql);
    }
}
