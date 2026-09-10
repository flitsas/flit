using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// HU #12371 (Feature #12150) — el radicado pasa de número pelado a <c>FT1-0000012</c>: prefijo
    /// de familia y consecutivo relleno a siete dígitos. DDL en
    /// <c>Persistence/Sql/Ddl/108-HU12371-radicado-prefijo-familia.sql</c>.
    ///
    /// Aparece una columna numérica <c>consecutivo</c> (la secuencia pasa a ser suya) por la que se
    /// ordena y se busca; <c>reference_number</c> conserva nombre y tipo y guarda el texto compuesto.
    /// Lo compone un trigger BEFORE INSERT, así que ningún camino de escritura puede componerlo
    /// distinto. El script es reejecutable: cada paso se guarda por el formato de la fila.
    ///
    /// <b>Misma trampa de despliegue que la HU #12151:</b> revertir el código sin revertir la
    /// migración deja de poder crearse trámites (el código viejo espera leer un número pelado y el
    /// CHECK nuevo exige el formato). Revertir código y migración juntos.
    /// </remarks>
    [DbContext(typeof(FlitDbContext))]
    [Migration("20260910160000_HU12371_RadicadoPrefijoFamilia")]
    public partial class HU12371_RadicadoPrefijoFamilia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedDdl.LoadUp("108-HU12371-radicado-prefijo-familia.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Devuelve el radicado al número pelado de las HU #12151/#12153 (mismo consecutivo, sin
            // prefijo ni relleno) y la estructura que aquellas dejaron: DEFAULT en la columna de
            // texto, CHECK numérico sin ceros a la izquierda e índice de apoyo al orden por longitud.
            // El número no se pierde: está en consecutivo, y es el mismo.
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS tr_procedure_instances_radicado_inmutable ON tramites.procedure_instances;
                DROP FUNCTION IF EXISTS tramites.fn_procedure_instances_radicado_inmutable();
                DROP TRIGGER IF EXISTS tr_procedure_instances_radicado ON tramites.procedure_instances;
                DROP FUNCTION IF EXISTS tramites.fn_procedure_instances_radicado();

                ALTER TABLE tramites.procedure_instances
                    DROP CONSTRAINT IF EXISTS ck_procedure_instances_reference_formato;
                ALTER TABLE tramites.procedure_instances
                    DROP CONSTRAINT IF EXISTS ck_procedure_instances_consecutivo_positivo;

                UPDATE tramites.procedure_instances
                   SET reference_number = consecutivo::text
                 WHERE reference_number ~ '^FT[1-9]-[0-9]+$';

                ALTER TABLE tramites.procedure_instances
                    ADD CONSTRAINT ck_procedure_instances_reference_numerico
                    CHECK (reference_number ~ '^[1-9][0-9]*$');

                CREATE INDEX IF NOT EXISTS ix_procedure_instances_reference_orden
                    ON tramites.procedure_instances (length(reference_number), reference_number);

                ALTER SEQUENCE tramites.procedure_instance_reference_seq
                    OWNED BY tramites.procedure_instances.reference_number;
                ALTER TABLE tramites.procedure_instances
                    ALTER COLUMN reference_number
                    SET DEFAULT nextval('tramites.procedure_instance_reference_seq')::text;

                DROP INDEX IF EXISTS tramites.uq_procedure_instances_consecutivo;
                ALTER TABLE tramites.procedure_instances
                    DROP COLUMN IF EXISTS consecutivo;
                """);
        }
    }
}
