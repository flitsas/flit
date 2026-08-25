using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// ADR-0050 — enciende <c>wizard_enabled</c> en los dos tipos canónicos, que son los que ya operaban
/// y cuyos pasos vienen del DDL 81. DDL: <c>85-habilitar-tipos-operativos.sql</c>.
/// <para>Desde que la barrera se hace cumplir en el servidor, sin esta migración no se puede crear
/// ningún trámite. El resto del catálogo se habilita uno a uno desde el configurador, cuando su
/// parametrización esté validada con negocio.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260824150000_HabilitarTiposOperativos")]
public partial class HabilitarTiposOperativos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("85-habilitar-tipos-operativos.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Vuelve a apagar los dos tipos, que es el estado que dejó el DDL 79. Revertir esta migración
    /// deja la operación sin ningún tipo creable — es lo correcto: el estado anterior era ese.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            UPDATE tramites.procedure_types
               SET wizard_enabled = false, updated_at = now()
             WHERE code IN ('MATRICULA_NUEVA', 'TRASPASO_STANDARD');
            """);
}
