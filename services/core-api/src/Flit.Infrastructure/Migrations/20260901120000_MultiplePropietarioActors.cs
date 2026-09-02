using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Multiple Propietario (ADR-0053, Propuesto): <c>ordinal</c> + <c>ownership_percentage</c> en
/// <c>tramites.procedure_instance_actors</c>, reemplazando el índice único
/// (instance, entity) por (instance, entity, ordinal). Aditiva: toda fila existente queda en
/// ordinal=1, ownership_percentage=NULL — comportamiento idéntico al actual. DDL:
/// <c>100-multiple-propietario-actors.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260901120000_MultiplePropietarioActors")]
public partial class MultiplePropietarioActors : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("100-multiple-propietario-actors.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Rollback condicionado (ADR-0053, Consecuencias): solo es seguro si ninguna fila quedó con
    /// <c>ordinal &gt; 1</c> (es decir, nunca se capturó un copropietario real). Con copropietarios
    /// reales, el Down aborta explícitamente en vez de truncar datos de reparto en silencio.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM tramites.procedure_instance_actors WHERE ordinal > 1)
                THEN
                    RAISE EXCEPTION
                        'Rollback de ADR-0053 abortado: existen actores con ordinal > 1 '
                        '(copropietarios reales). El Down solo es seguro cuando todas las filas '
                        'estan en ordinal=1.';
                END IF;
            END $$;

            DROP INDEX IF EXISTS tramites.uq_procedure_instance_actors_instance_entity_ordinal;

            ALTER TABLE tramites.procedure_instance_actors
                ADD CONSTRAINT uq_procedure_instance_actors_instance_entity
                UNIQUE (procedure_instance_id, procedure_entity_id);

            ALTER TABLE tramites.procedure_instance_actors
                DROP CONSTRAINT IF EXISTS ck_procedure_instance_actors_ownership_pct;

            ALTER TABLE tramites.procedure_instance_actors
                DROP CONSTRAINT IF EXISTS ck_procedure_instance_actors_ordinal;

            ALTER TABLE tramites.procedure_instance_actors
                DROP COLUMN IF EXISTS ownership_percentage;

            ALTER TABLE tramites.procedure_instance_actors
                DROP COLUMN IF EXISTS ordinal;
            """);
}
