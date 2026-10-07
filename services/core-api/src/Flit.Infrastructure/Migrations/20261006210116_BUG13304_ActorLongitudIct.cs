using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Bug #13304 (numeral 12) — el actor del trámite acepta lo mismo que envía ICT: nombre 320 y teléfono 50
/// (<c>procedure_instance_actors.full_name/phone</c>), y <c>procedure_instances.vendedor_nombre/comprador_nombre</c>
/// a 320 porque los llena el trigger de denormalización, y <c>procedure_instance_biometric_validations.name</c> a 320
/// porque recibe el nombre del actor al crear la validación en Kyverum. Recrea <c>analytics.v_procedure_detail_report</c>
/// (lee <c>full_name</c>; Postgres no deja cambiar el tipo de una columna usada por una vista).
/// DDL: <c>132-BUG13304-actor-longitud-ict.sql</c>. Idempotente; ampliar un varchar no reescribe la tabla.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20261006210116_BUG13304_ActorLongitudIct")]
public partial class BUG13304_ActorLongitudIct : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("132-BUG13304-actor-longitud-ict.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Vuelve a 200/20 (y <c>name</c> de la validación biométrica a 200) y recrea la vista con el DDL 90 (definición idéntica a la del DDL 132).
    /// <b>Falla con 22001 si ya hay filas con nombre &gt; 200 o teléfono &gt; 20</b>: es a propósito, el Down
    /// no trunca datos personales en silencio. Para revertir con datos así hay que decidir antes qué hacer con ellos.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP VIEW IF EXISTS analytics.v_procedure_detail_report;
            ALTER TABLE tramites.procedure_instance_biometric_validations
                ALTER COLUMN name TYPE varchar(200);
            ALTER TABLE tramites.procedure_instances
                ALTER COLUMN vendedor_nombre TYPE varchar(200),
                ALTER COLUMN comprador_nombre TYPE varchar(200);
            ALTER TABLE tramites.procedure_instance_actors
                ALTER COLUMN full_name TYPE varchar(200),
                ALTER COLUMN phone TYPE varchar(20);
            """);
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("90-bi-view-traspaso-solo-en-familia-traspaso.sql"));
    }
}
