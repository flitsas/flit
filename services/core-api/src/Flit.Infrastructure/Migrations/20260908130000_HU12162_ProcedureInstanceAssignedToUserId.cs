using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// HU #12162 (Feature #12155, Dashboard admin) — agrega <c>assigned_to_user_id</c> a
/// <c>tramites.procedure_instances</c>: el gestor actualmente responsable del trámite, reasignable
/// por un admin (permiso <c>AdminTramiteReasignarGestor</c>, catálogo HU #12157). Distinto de
/// <c>created_by_user_id</c> (quién radicó, auditoría inmutable — no se toca). Nullable, sin
/// backfill del creador (ver nota de decisión en el DDL). FK <c>identity.users(id)</c> ON DELETE SET
/// NULL. DDL: <c>104-HU12162-reasignar-gestor.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260908130000_HU12162_ProcedureInstanceAssignedToUserId")]
public partial class HU12162_ProcedureInstanceAssignedToUserId : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("104-HU12162-reasignar-gestor.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Aditiva y reversible sin condiciones: la columna es nueva y nunca tuvo backfill, así que
    /// eliminarla no pierde ningún dato que existiera antes de esta migración.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS tramites.ix_procedure_instances_tenant_id_assigned_to_user_id;

            ALTER TABLE tramites.procedure_instances
                DROP CONSTRAINT IF EXISTS fk_procedure_instances_assigned_to_user;

            ALTER TABLE tramites.procedure_instances
                DROP COLUMN IF EXISTS assigned_to_user_id;
            """);
}
