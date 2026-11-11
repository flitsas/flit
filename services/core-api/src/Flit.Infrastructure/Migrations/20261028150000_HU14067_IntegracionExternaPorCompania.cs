using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #14067 (Feature H, Épica #14021) — integración externa «Persona Natural» ligada a la compañía: columnas
/// <c>tipo</c> y <c>company_id</c> en <c>integrations.external_clients</c> e índice único parcial por (compañía, tipo)
/// para las integraciones activas. Aditiva e idempotente; las filas existentes quedan <c>tipo='externa'</c> sin compañía.
/// DDL: <c>162-HU14067-integracion-externa-por-compania.sql</c>. Lleva los atributos inline (sin Designer): sin ellos la
/// migración no se descubre y no corre al arrancar.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261028150000_HU14067_IntegracionExternaPorCompania")]
public partial class HU14067_IntegracionExternaPorCompania : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("162-HU14067-integracion-externa-por-compania.sql"));

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS integrations.ix_external_clients_company;
            DROP INDEX IF EXISTS integrations.uq_external_clients_company_tipo_activo;

            -- Las integraciones ligadas a una compañía no tienen representación sin las columnas: se retiran antes.
            DELETE FROM integrations.external_clients WHERE tipo <> 'externa';

            ALTER TABLE integrations.external_clients
                DROP CONSTRAINT IF EXISTS fk_external_clients_company,
                DROP CONSTRAINT IF EXISTS ck_external_clients_company_por_tipo,
                DROP CONSTRAINT IF EXISTS ck_external_clients_tipo;
            ALTER TABLE integrations.external_clients
                DROP COLUMN IF EXISTS company_id,
                DROP COLUMN IF EXISTS tipo;
            """);
}
