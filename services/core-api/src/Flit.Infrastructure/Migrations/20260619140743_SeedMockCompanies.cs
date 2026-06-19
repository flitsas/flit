using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Infrastructure.Migrations
{
    /// <summary>
    /// Datos mock de compañías (<c>identity.tenants</c>) para poder probar la consola
    /// de administración y el alta de compañías en DEV/QA. Migración de SOLO datos:
    /// no crea ni altera esquema. Idempotente (<c>ON CONFLICT DO NOTHING</c>) y
    /// reversible (el Down borra exactamente las filas sembradas por su id).
    ///
    /// Solo siembra <c>identity.tenants</c> (la tabla que impacta el alta). La config
    /// operativa, whitelist y OT de estas compañías arrancan vacías a propósito: la
    /// consola ya maneja ese estado "sin configurar" y se completan desde la UI.
    /// </summary>
    public partial class SeedMockCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                INSERT INTO identity.tenants
                    (id, code, legal_name, tax_id, tenant_type, is_active, created_at)
                VALUES
                    ('0ad1c0de-0000-4000-8000-000000000001', 'MOCK-RENTANDINO',    'Renting Andino S.A.S.',            '900123456-1', 'RENTING',       true,  TIMESTAMPTZ '2026-01-15 10:00:00+00'),
                    ('0ad1c0de-0000-4000-8000-000000000002', 'MOCK-AUTOPACIFICO',  'Concesionario AutoPacífico S.A.',  '901234567-2', 'CONCESIONARIO', true,  TIMESTAMPTZ '2026-02-03 09:30:00+00'),
                    ('0ad1c0de-0000-4000-8000-000000000003', 'MOCK-MOVCARIBE',     'Movilidad Caribe Renting S.A.S.',  '830111222-3', 'RENTING',       true,  TIMESTAMPTZ '2026-02-20 14:15:00+00'),
                    ('0ad1c0de-0000-4000-8000-000000000004', 'MOCK-FLITOPS',       'FLIT Operaciones S.A.S.',          '901999888-4', 'FLIT',          true,  TIMESTAMPTZ '2026-03-01 08:00:00+00'),
                    ('0ad1c0de-0000-4000-8000-000000000005', 'MOCK-DISTRILLANO',   'Distrirentas del Llano S.A.S.',    '891777666-5', 'RENTING',       false, TIMESTAMPTZ '2026-03-18 16:45:00+00'),
                    ('0ad1c0de-0000-4000-8000-000000000006', 'MOCK-NORTEMOTORS',   'Concesionario Norte Motors S.A.S.','805444333-6', 'CONCESIONARIO', true,  TIMESTAMPTZ '2026-04-05 11:20:00+00'),
                    ('0ad1c0de-0000-4000-8000-000000000007', 'MOCK-LEASINGBOG',    'Leasing Vehicular Bogotá S.A.S.',  '860555444-7', 'RENTING',       true,  TIMESTAMPTZ '2026-04-22 13:10:00+00'),
                    ('0ad1c0de-0000-4000-8000-000000000008', 'MOCK-AUTOFLOTA',     'AutoFlota Antioquia S.A.S.',       '811222333-8', 'CONCESIONARIO', false, TIMESTAMPTZ '2026-05-09 15:00:00+00')
                ON CONFLICT DO NOTHING;
                """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                """
                DELETE FROM identity.tenants
                WHERE id IN (
                    '0ad1c0de-0000-4000-8000-000000000001',
                    '0ad1c0de-0000-4000-8000-000000000002',
                    '0ad1c0de-0000-4000-8000-000000000003',
                    '0ad1c0de-0000-4000-8000-000000000004',
                    '0ad1c0de-0000-4000-8000-000000000005',
                    '0ad1c0de-0000-4000-8000-000000000006',
                    '0ad1c0de-0000-4000-8000-000000000007',
                    '0ad1c0de-0000-4000-8000-000000000008'
                );
                """);
    }
}
