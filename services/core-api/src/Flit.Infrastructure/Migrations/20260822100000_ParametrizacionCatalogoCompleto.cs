using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Sql;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// ADR-0050 / CFD-09 — pasos y secciones del resto del catálogo canónico (17 tipos), para que el
/// wizard pueda conformarse desde el tipo en lugar de caer al recorrido estático heredado.
/// DDL: <c>82-parametrizacion-catalogo-completo.sql</c>.
/// <para><b>Base técnica, no diseño funcional validado.</b> Ningún tipo queda con
/// <c>wizard_enabled = true</c>: siguen sin poder elegirse al crear un trámite, así que esta
/// migración no cambia lo que el gestor puede hacer hoy.</para>
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260822100000_ParametrizacionCatalogoCompleto")]
public partial class ParametrizacionCatalogoCompleto : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(EmbeddedDdl.LoadUp("82-parametrizacion-catalogo-completo.sql"));

    /// <inheritdoc />
    /// <remarks>
    /// Retira los pasos sembrados. No restaura <c>gate_profile</c> a su valor previo: era <c>{}</c>
    /// en todos estos tipos —ninguno estaba configurado— así que se vacía.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            DELETE FROM tramites.procedure_steps
             WHERE procedure_type_id IN (
                 SELECT id FROM tramites.procedure_types
                  WHERE code IN (
                      'MATRICULA_LEASING', 'CANCELACION_MATRICULA', 'REMATRICULA',
                      'TRASPASO_UNILATERAL', 'TRASPASO_TRANSFERENCIA_DE_DOMINIO',
                      'CAMBIO_CARROCERIA', 'BLINDAJE', 'CAMBIO_COLOR', 'DUPLICADO_TARJETA',
                      'RADICADO_CUENTA', 'CONVERSION_COMBUSTIBLE', 'TRASLADO_CUENTA',
                      'REGRABAR_MOTOR_CHASIS', 'DUPLICADO_PLACA',
                      'LEVANTAMIENTO_PRENDA', 'LEVANTAR_INSCRIBIR_PRENDA', 'CAMBIO_ACREEDOR'));

            UPDATE tramites.procedure_types
               SET gate_profile = '{}'::jsonb, updated_at = now()
             WHERE code IN (
                 'MATRICULA_LEASING', 'CANCELACION_MATRICULA', 'REMATRICULA',
                 'TRASPASO_UNILATERAL', 'TRASPASO_TRANSFERENCIA_DE_DOMINIO',
                 'CAMBIO_CARROCERIA', 'BLINDAJE', 'CAMBIO_COLOR', 'DUPLICADO_TARJETA',
                 'RADICADO_CUENTA', 'CONVERSION_COMBUSTIBLE', 'TRASLADO_CUENTA',
                 'REGRABAR_MOTOR_CHASIS', 'DUPLICADO_PLACA',
                 'LEVANTAMIENTO_PRENDA', 'LEVANTAR_INSCRIBIR_PRENDA', 'CAMBIO_ACREEDOR');
            """);
}
