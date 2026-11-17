using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Consultas.Api.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Solo datos. Decisión del usuario 2026-11-16 (reemplaza P7): la consulta de impuesto queda encendida solo en
    /// 05 Antioquia, 08 Atlántico y 11 Bogotá; el resto se apaga con borrado lógico. Pisa lo que un operador haya
    /// encendido o apagado por API en el ambiente. Los nombres siguen <c>34-transit-offices-city-department-names.sql</c>.
    /// El Down no restaura lo encendido por API fuera de 05/08/11 ni el estado previo de una fila 05/08/11 del operador:
    /// antes de aplicar, guardar el estado con <c>SELECT * FROM consultas.vehicle_tax_departments</c>.
    /// </remarks>
    public partial class ImpuestoDepartamentosActivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Una fila que ya exista (p. ej. creada por API) conserva su id y su nombre; solo se enciende.
            migrationBuilder.Sql(
                """
                INSERT INTO consultas.vehicle_tax_departments
                    (id, department_code, department_name, is_active, created_at, updated_at, deleted_at)
                VALUES
                    ('01941215-0000-7000-8000-000000000005', '05', 'ANTIOQUIA',   true, now(), now(), NULL),
                    ('01941215-0000-7000-8000-000000000008', '08', 'ATLANTICO',   true, now(), now(), NULL),
                    ('01941215-0000-7000-8000-000000000011', '11', 'BOGOTA D.C.', true, now(), now(), NULL)
                ON CONFLICT (department_code) DO UPDATE
                    SET is_active = true, deleted_at = NULL, updated_at = now();

                UPDATE consultas.vehicle_tax_departments
                   SET is_active = false, deleted_at = COALESCE(deleted_at, now()), updated_at = now()
                 WHERE department_code NOT IN ('05', '08', '11')
                   AND (is_active OR deleted_at IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Borra solo las filas que insertó el Up (id determinista); una fila previa de 05/08/11 tiene otro id y se apaga.
            // Luego deja la semilla de 20261104150611 como estaba: 50, 52 y 94 activos; 17 y 63 con su borrado original.
            migrationBuilder.Sql(
                """
                DELETE FROM consultas.vehicle_tax_departments
                 WHERE id IN ('01941215-0000-7000-8000-000000000005',
                              '01941215-0000-7000-8000-000000000008',
                              '01941215-0000-7000-8000-000000000011');

                UPDATE consultas.vehicle_tax_departments
                   SET is_active = false, deleted_at = COALESCE(deleted_at, now()), updated_at = now()
                 WHERE department_code IN ('05', '08', '11');

                INSERT INTO consultas.vehicle_tax_departments
                    (id, department_code, department_name, is_active, created_at, updated_at, deleted_at)
                VALUES
                    ('01941215-0000-7000-8000-000000000017', '17', 'CALDAS',  false, TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', now(), TIMESTAMPTZ '2026-01-15 18:15:19.862895+00'),
                    ('01941215-0000-7000-8000-000000000094', '94', 'GUAINIA', true,  TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', now(), NULL),
                    ('01941215-0000-7000-8000-000000000050', '50', 'META',    true,  TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', now(), NULL),
                    ('01941215-0000-7000-8000-000000000052', '52', 'NARIÑO',  true,  TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', now(), NULL),
                    ('01941215-0000-7000-8000-000000000063', '63', 'QUINDIO', false, TIMESTAMPTZ '2025-12-15 16:49:19.862895+00', now(), TIMESTAMPTZ '2026-01-22 09:02:19.862895+00')
                ON CONFLICT (department_code) DO UPDATE
                    SET is_active = EXCLUDED.is_active, deleted_at = EXCLUDED.deleted_at, updated_at = now();
                """);
        }
    }
}
