using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// Siembra los tipos de trámite canónicos (<c>tramites.procedure_types</c>) que la
    /// consola documental necesita para asociar documentos. La lista estática del frontend
    /// (<c>frontend/lib/constants/procedure-types.ts</c>) apunta a estos ids fijos; sin estas
    /// filas, asociar un documento a un trámite falla con 404 en DEV/QA/prod (el DDL HU10151
    /// crea la tabla pero no la siembra).
    ///
    /// Migración de SOLO datos: no crea ni altera esquema. Idempotente
    /// (<c>ON CONFLICT DO NOTHING</c>) y reversible (el Down borra exactamente las filas
    /// sembradas por su id). <c>family = VEHICULAR</c> es obligatorio (columna NOT NULL).
    /// </summary>
    public partial class SeedProcedureTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                INSERT INTO tramites.procedure_types
                    (id, code, name, family, is_active)
                VALUES
                    ('33333333-3333-3333-3333-333333333333', 'TRASPASO',          'Traspaso',           'VEHICULAR', true),
                    ('44444444-4444-4444-4444-444444444444', 'MATRICULA_INICIAL', 'Matrícula inicial',  'VEHICULAR', true),
                    ('55555555-5555-5555-5555-555555555555', 'DUPLICADO_PLACA',   'Duplicado de placa', 'VEHICULAR', true),
                    ('66666666-6666-6666-6666-666666666666', 'CAMBIO_SERVICIO',   'Cambio de servicio', 'VEHICULAR', true)
                ON CONFLICT DO NOTHING;
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                DELETE FROM tramites.procedure_types
                WHERE id IN (
                    '33333333-3333-3333-3333-333333333333',
                    '44444444-4444-4444-4444-444444444444',
                    '55555555-5555-5555-5555-555555555555',
                    '66666666-6666-6666-6666-666666666666'
                );
                """);
    }
}
