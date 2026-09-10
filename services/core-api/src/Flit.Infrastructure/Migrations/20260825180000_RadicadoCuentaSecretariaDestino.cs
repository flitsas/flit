using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Declara que en <c>RADICADO_CUENTA</c> el organismo de tránsito lo ELIGE el operador (es el
/// destino, y quien aprueba), y mueve el organismo del RUNT de los borradores en curso a las claves
/// descriptivas <c>transit_office_actual_*</c>. DDL: <c>92-radicado-cuenta-secretaria-destino.sql</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260825180000_RadicadoCuentaSecretariaDestino")]
public partial class RadicadoCuentaSecretariaDestino : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("92-radicado-cuenta-secretaria-destino.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Retira la llave (sin ella el organismo vuelve a deducirse del modo de entrada: PLACA ⇒ lo
    /// impone el RUNT) y devuelve a las claves canónicas el organismo que se movió a las
    /// descriptivas. Un destino que el gestor hubiera elegido entretanto se pierde: es inherente a
    /// revertir la separación de los dos conceptos, no un descuido.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            UPDATE tramites.procedure_types
               SET gate_profile = gate_profile - 'transitOfficeSource',
                   updated_at = now()
             WHERE code = 'RADICADO_CUENTA';

            DELETE FROM tramites.procedure_instance_field_values fv
             USING tramites.procedure_instances pi, tramites.procedure_types pt
             WHERE fv.procedure_instance_id = pi.id
               AND pi.procedure_type_id = pt.id
               AND pt.code = 'RADICADO_CUENTA'
               AND fv.field_key IN ('transit_office_id', 'transit_office_code',
                                    'transit_office_name', 'transit_office_city');

            UPDATE tramites.procedure_instance_field_values fv
               SET field_key = replace(fv.field_key, 'transit_office_actual_', 'transit_office_'),
                   updated_at = now()
              FROM tramites.procedure_instances pi
              JOIN tramites.procedure_types pt ON pt.id = pi.procedure_type_id
             WHERE fv.procedure_instance_id = pi.id
               AND pt.code = 'RADICADO_CUENTA'
               AND fv.field_key LIKE 'transit_office_actual_%';
            """);
}
