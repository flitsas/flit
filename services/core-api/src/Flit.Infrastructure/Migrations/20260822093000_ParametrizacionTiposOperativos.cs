using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// ADR-0050 / CFD-09 — pasos y secciones reales de <c>MATRICULA_NUEVA</c> (5) y
/// <c>TRASPASO_STANDARD</c> (6), con los mismos códigos de paso que emite el camino estático.
/// DDL: <c>81-parametrizacion-tipos-operativos.sql</c>.
/// <para>Sin esto, encender <c>F08_DynamicProcedures</c> colapsa el wizard a un único paso
/// <c>generic_form</c> siempre completo: es el prerequisito de datos para que el motor dinámico
/// alcance paridad con el estático.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260822093000_ParametrizacionTiposOperativos")]
public partial class ParametrizacionTiposOperativos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("81-parametrizacion-tipos-operativos.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Devuelve los dos tipos a la configuración mínima de los seeds DEV (un paso, una sección
    /// <c>generic_form</c>). No se restauran los <c>form_fields</c> uno a uno: el <c>Up</c> los
    /// recrea por completo, así que el estado tras Down→Up vuelve a ser el mismo.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DELETE FROM tramites.procedure_steps
             WHERE procedure_type_id IN (
                 SELECT id FROM tramites.procedure_types
                  WHERE code IN ('MATRICULA_NUEVA', 'TRASPASO_STANDARD'));

            INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active)
            SELECT uuidv7(), pt.id, 'DATOS_VEHICULO', 'Datos del vehículo', 1, true
              FROM tramites.procedure_types pt
             WHERE pt.code = 'MATRICULA_NUEVA';

            INSERT INTO tramites.procedure_steps (id, procedure_type_id, code, title, sort_order, is_active)
            SELECT uuidv7(), pt.id, 'CONSULTA_PLACA', 'Consulta por placa', 1, true
              FROM tramites.procedure_types pt
             WHERE pt.code = 'TRASPASO_STANDARD';

            INSERT INTO tramites.procedure_sections (id, procedure_step_id, code, title, sort_order, layout)
            SELECT uuidv7(), s.id, 'IDENTIFICACION', 'Identificación del vehículo', 1, 'single'
              FROM tramites.procedure_steps s
              JOIN tramites.procedure_types pt ON pt.id = s.procedure_type_id
             WHERE pt.code IN ('MATRICULA_NUEVA', 'TRASPASO_STANDARD');
            """);
}
