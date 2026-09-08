using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations;

/// <summary>
/// Siembra el copy de <c>tramites.procedure_types.description</c> para Matrícula Inicial y Traspaso
/// (AC3 de HU #12126). HU #12126 eliminó el texto hardcodeado que <c>infoTextNuevoTramite</c> tenía
/// para estas dos familias (en <c>nuevo-tramite-resolver.ts</c>, antes del commit 345987a1) y lo
/// reemplazó por lectura de <c>Description</c> — esta migración traslada exactamente ese mismo texto
/// a la base de datos para que no desaparezca de la franja informativa del WIZARD al desplegar.
///
/// Texto recuperado del código previo a HU #12126 (<c>git show 345987a1^:frontend/lib/tramites/nuevo-tramite-resolver.ts</c>),
/// sin alterar una palabra. Cubre los codes activos (<c>MATRICULA_NUEVA</c>, <c>MATRICULA_LEASING</c>,
/// <c>TRASPASO_STANDARD</c>, <c>TRASPASO_UNILATERAL</c>) y los alias legacy que
/// <c>CODES_MATRICULA_STD</c>/<c>CODES_TRASPASO_BILATERAL</c> también aceptan como fallback
/// (<c>MATRICULA_INICIAL</c>, <c>TRASPASO_BILATERAL</c>, <c>TRASPASO</c>), para que el copy no
/// dependa de cuál code exacto tenga habilitado cada organismo/compañía.
///
/// Idempotente y no destructivo con ediciones manuales: mismo patrón que
/// <c>20260907120000_HU12125_SeedOtrosTramitesDescriptions</c> — el <c>UPDATE</c> solo reemplaza
/// <c>description</c> cuando está en <c>NULL</c> o cuando coincide EXACTAMENTE con el placeholder
/// genérico sembrado originalmente para cada code (<c>04-HU10151-seeds-minimos.sql</c> para
/// MATRICULA_NUEVA/TRASPASO_STANDARD, <c>78-seed-tipos-tramite-leasing.sql</c>/
/// <c>40-catalogo-tipos-tramite-canonico.sql</c> para MATRICULA_LEASING/TRASPASO_UNILATERAL).
/// <c>Down</c> revierte a <c>NULL</c> solo si el valor coincide exactamente con lo sembrado por
/// este <c>Up</c>.
/// </summary>
[DbContext(typeof(FlitDbContext))]
[Migration("20260907130000_HU12126_SeedMatriculaTraspasoDescriptions")]
public partial class HU12126_SeedMatriculaTraspasoDescriptions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE tramites.procedure_types SET description = CASE code
                WHEN 'MATRICULA_NUEVA' THEN 'Matrícula tradicional: el vehículo nuevo será matriculado a nombre del comprador ante el organismo de tránsito elegido.'
                WHEN 'MATRICULA_INICIAL' THEN 'Matrícula tradicional: el vehículo nuevo será matriculado a nombre del comprador ante el organismo de tránsito elegido.'
                WHEN 'MATRICULA_LEASING' THEN 'Matrícula tipo Leasing: el vehículo queda registrado a nombre de la entidad financiera (arrendador), mientras lo usas como locatario según el contrato.'
                WHEN 'TRASPASO_STANDARD' THEN 'Traspaso bilateral: traspaso vehicular donde el comprador y el vendedor radican ante el organismo de tránsito.'
                WHEN 'TRASPASO_BILATERAL' THEN 'Traspaso bilateral: traspaso vehicular donde el comprador y el vendedor radican ante el organismo de tránsito.'
                WHEN 'TRASPASO' THEN 'Traspaso bilateral: traspaso vehicular donde el comprador y el vendedor radican ante el organismo de tránsito.'
                WHEN 'TRASPASO_UNILATERAL' THEN 'Traspaso unilateral: traspaso realizado únicamente por el propietario actual, sin requerir la presencia del comprador en la sede de tránsito.'
            END,
            updated_at = now()
            WHERE code IN (
                'MATRICULA_NUEVA', 'MATRICULA_INICIAL', 'MATRICULA_LEASING',
                'TRASPASO_STANDARD', 'TRASPASO_BILATERAL', 'TRASPASO', 'TRASPASO_UNILATERAL'
            )
            AND (
                description IS NULL
                OR (code, description) IN (
                    ('MATRICULA_NUEVA', 'Matrícula inicial de vehículo.'),
                    ('MATRICULA_LEASING', 'Matrícula con locatario.'),
                    ('TRASPASO_STANDARD', 'Traspaso de propiedad.'),
                    ('TRASPASO_UNILATERAL', 'Traspaso unilateral a locatario.')
                )
            );
            """);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            UPDATE tramites.procedure_types SET description = NULL, updated_at = now()
            WHERE (code, description) IN (
                ('MATRICULA_NUEVA', 'Matrícula tradicional: el vehículo nuevo será matriculado a nombre del comprador ante el organismo de tránsito elegido.'),
                ('MATRICULA_INICIAL', 'Matrícula tradicional: el vehículo nuevo será matriculado a nombre del comprador ante el organismo de tránsito elegido.'),
                ('MATRICULA_LEASING', 'Matrícula tipo Leasing: el vehículo queda registrado a nombre de la entidad financiera (arrendador), mientras lo usas como locatario según el contrato.'),
                ('TRASPASO_STANDARD', 'Traspaso bilateral: traspaso vehicular donde el comprador y el vendedor radican ante el organismo de tránsito.'),
                ('TRASPASO_BILATERAL', 'Traspaso bilateral: traspaso vehicular donde el comprador y el vendedor radican ante el organismo de tránsito.'),
                ('TRASPASO', 'Traspaso bilateral: traspaso vehicular donde el comprador y el vendedor radican ante el organismo de tránsito.'),
                ('TRASPASO_UNILATERAL', 'Traspaso unilateral: traspaso realizado únicamente por el propietario actual, sin requerir la presencia del comprador en la sede de tránsito.')
            );
            """);
}
