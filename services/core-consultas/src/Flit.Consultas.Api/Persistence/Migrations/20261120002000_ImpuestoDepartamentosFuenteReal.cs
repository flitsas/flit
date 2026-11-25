using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Consultas.Api.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Solo datos (D2). Enciende 05/13/17/63/68/76 y apaga el resto. Pisa lo encendido por API: guardar
    /// <c>SELECT * FROM consultas.vehicle_tax_departments</c> antes de aplicar. Down vuelve al estado de 20261117044000.
    /// </remarks>
    [DbContext(typeof(ConsultasDb))]
    [Migration("20261120002000_ImpuestoDepartamentosFuenteReal")]
    public partial class ImpuestoDepartamentosFuenteReal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO consultas.vehicle_tax_departments (id, department_code, department_name, is_active, created_at, updated_at, deleted_at) VALUES
                  ('01941215-0000-7000-8000-000000000005', '05', 'ANTIOQUIA',       true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000013', '13', 'BOLIVAR',         true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000017', '17', 'CALDAS',          true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000063', '63', 'QUINDIO',         true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000068', '68', 'SANTANDER',       true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000076', '76', 'VALLE DEL CAUCA', true, now(), now(), NULL)
                ON CONFLICT (department_code) DO UPDATE SET is_active = true, deleted_at = NULL, updated_at = now();

                UPDATE consultas.vehicle_tax_departments
                   SET is_active = false, deleted_at = COALESCE(deleted_at, now()), updated_at = now()
                 WHERE department_code NOT IN ('05', '13', '17', '63', '68', '76')
                   AND (is_active OR deleted_at IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM consultas.vehicle_tax_departments
                 WHERE id IN ('01941215-0000-7000-8000-000000000013', '01941215-0000-7000-8000-000000000068', '01941215-0000-7000-8000-000000000076');
                UPDATE consultas.vehicle_tax_departments
                   SET is_active = false, deleted_at = COALESCE(deleted_at, now()), updated_at = now()
                 WHERE department_code IN ('17', '63');
                UPDATE consultas.vehicle_tax_departments
                   SET is_active = true, deleted_at = NULL, updated_at = now()
                 WHERE department_code IN ('05', '08', '11');
                """);
        }
    }
}
