using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Siembra el copy de <c>tramites.procedure_types.description</c> para los 12 tipos de trámite de
/// la familia "Otros Trámites" (AC3 de HU #12125). La columna y su uso en el CRUD del configurador
/// ya existen (HU #12124/#12125); esta migración solo carga el VALOR inicial.
///
/// Migración de SOLO datos (UPDATE): no crea ni altera esquema. Como <c>procedure_types</c> está
/// excluida del snapshot EF (<c>ExcludeFromMigrations</c> en <c>ProcedureTypeConfiguration</c>, DDL
/// gestionado por SQL crudo desde HU #10151), esta clase no necesita <c>.Designer.cs</c>: usa los
/// atributos inline <c>[DbContext]</c>/<c>[Migration]</c> para que EF la descubra y la corra al
/// arranque, igual que <c>CatalogoTiposTramiteCanonico</c> (patrón para migraciones donde el diff
/// del modelo EF sale vacío).
///
/// Idempotente y no destructivo con ediciones manuales: el <c>UPDATE</c> filtra
/// <c>WHERE description IS NULL</c>, así que solo siembra el copy inicial una vez por fila y nunca
/// pisa una descripción que ya haya sido editada a mano desde el configurador. Correrla varias
/// veces no tiene efecto adicional tras la primera vez que puebla cada fila.
///
/// Reversible: el <c>Down</c> vuelve <c>description</c> a <c>NULL</c> únicamente en las filas cuyo
/// valor coincide exactamente con el copy sembrado por este <c>Up</c> (match por <c>code</c> +
/// valor exacto), para no borrar una edición manual posterior que coincidiera por casualidad con
/// otro código de la lista.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260907120000_HU12125_SeedOtrosTramitesDescriptions")]
public partial class HU12125_SeedOtrosTramitesDescriptions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE tramites.procedure_types SET description = CASE code
                WHEN 'BLINDAJE' THEN 'Registro oficial cuando el vehículo recibe protección antibalas o refuerzos especiales. Garantiza que cumple normas de seguridad y queda legalmente reconocido.'
                WHEN 'CAMBIO_CARROCERIA' THEN 'Se actualiza la estructura externa del vehículo (ejemplo: de sedán a camioneta). Es obligatorio porque cambia las características técnicas registradas.'
                WHEN 'CAMBIO_COLOR' THEN 'Se reporta el nuevo color del vehículo tras pintura o modificación. Mantiene la tarjeta de propiedad coherente con la apariencia real.'
                WHEN 'CAMBIO_LOCATARIO' THEN 'Actualiza el responsable o la ciudad donde está registrado el vehículo. Útil cuando cambia de propietario dentro de una empresa o se traslada a otro municipio.'
                WHEN 'CANCELACION_MATRICULA' THEN 'Retira el vehículo del registro nacional, normalmente por pérdida total, desintegración o exportación. Significa que ya no puede circular legalmente.'
                WHEN 'CONVERSION_COMBUSTIBLE' THEN 'Registra el cambio del sistema de gasolina a gas natural vehicular u otro. Garantiza que cumple normas técnicas y ambientales.'
                WHEN 'DUPLICADO_PLACA' THEN 'Solicita una nueva placa cuando la original se pierde, se daña o se deteriora. Se entrega con el mismo número registrado.'
                WHEN 'DUPLICADO_TARJETA' THEN 'Emite una nueva tarjeta de propiedad por pérdida, robo o deterioro. No cambia la información del vehículo, solo reemplaza el documento físico.'
                WHEN 'PRENDA_INSCRIPCION' THEN 'Registra que el vehículo está como garantía de un crédito o financiación. Aparece en el sistema hasta que se pague la deuda.'
                WHEN 'LEVANTAMIENTO_PRENDA' THEN 'Elimina la prenda registrada cuando el crédito ha sido cancelado. Deja el vehículo libre de gravámenes.'
                WHEN 'RADICADO_CUENTA' THEN 'Registro inicial o actualización de la cuenta del propietario ante tránsito. Es requisito para realizar otros trámites o pagos.'
                WHEN 'TRASLADO_CUENTA' THEN 'Mueve el registro del vehículo de un organismo de tránsito a otro, generalmente por cambio de ciudad o departamento.'
            END,
            updated_at = now()
            WHERE code IN (
                'BLINDAJE', 'CAMBIO_CARROCERIA', 'CAMBIO_COLOR', 'CAMBIO_LOCATARIO',
                'CANCELACION_MATRICULA', 'CONVERSION_COMBUSTIBLE', 'DUPLICADO_PLACA',
                'DUPLICADO_TARJETA', 'PRENDA_INSCRIPCION', 'LEVANTAMIENTO_PRENDA',
                'RADICADO_CUENTA', 'TRASLADO_CUENTA'
            )
            AND description IS NULL;
            """);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE tramites.procedure_types SET description = NULL, updated_at = now()
            WHERE (code, description) IN (
                ('BLINDAJE', 'Registro oficial cuando el vehículo recibe protección antibalas o refuerzos especiales. Garantiza que cumple normas de seguridad y queda legalmente reconocido.'),
                ('CAMBIO_CARROCERIA', 'Se actualiza la estructura externa del vehículo (ejemplo: de sedán a camioneta). Es obligatorio porque cambia las características técnicas registradas.'),
                ('CAMBIO_COLOR', 'Se reporta el nuevo color del vehículo tras pintura o modificación. Mantiene la tarjeta de propiedad coherente con la apariencia real.'),
                ('CAMBIO_LOCATARIO', 'Actualiza el responsable o la ciudad donde está registrado el vehículo. Útil cuando cambia de propietario dentro de una empresa o se traslada a otro municipio.'),
                ('CANCELACION_MATRICULA', 'Retira el vehículo del registro nacional, normalmente por pérdida total, desintegración o exportación. Significa que ya no puede circular legalmente.'),
                ('CONVERSION_COMBUSTIBLE', 'Registra el cambio del sistema de gasolina a gas natural vehicular u otro. Garantiza que cumple normas técnicas y ambientales.'),
                ('DUPLICADO_PLACA', 'Solicita una nueva placa cuando la original se pierde, se daña o se deteriora. Se entrega con el mismo número registrado.'),
                ('DUPLICADO_TARJETA', 'Emite una nueva tarjeta de propiedad por pérdida, robo o deterioro. No cambia la información del vehículo, solo reemplaza el documento físico.'),
                ('PRENDA_INSCRIPCION', 'Registra que el vehículo está como garantía de un crédito o financiación. Aparece en el sistema hasta que se pague la deuda.'),
                ('LEVANTAMIENTO_PRENDA', 'Elimina la prenda registrada cuando el crédito ha sido cancelado. Deja el vehículo libre de gravámenes.'),
                ('RADICADO_CUENTA', 'Registro inicial o actualización de la cuenta del propietario ante tránsito. Es requisito para realizar otros trámites o pagos.'),
                ('TRASLADO_CUENTA', 'Mueve el registro del vehículo de un organismo de tránsito a otro, generalmente por cambio de ciudad o departamento.')
            );
            """);
}
