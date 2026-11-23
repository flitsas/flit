using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Consultas.Api.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Solo datos (P5' del 2026-11-23). Enciende los 33 códigos DANE (32 departamentos y Bogotá D.C.): la cobertura real la decide
    /// la API de impuesto (422 <c>department_unsupported</c>, 409 <c>source_inactive</c>). Una fila que ya exista conserva su id y su
    /// nombre; solo se enciende. Pisa lo apagado por API: guardar <c>SELECT * FROM consultas.vehicle_tax_departments</c> antes de
    /// aplicar. Down vuelve al estado de 20261120002000 (05/13/17/63/68/76 activos) y borra solo las filas de id determinista que
    /// insertó este Up (las 22 que no tenían fila).
    /// </remarks>
    [DbContext(typeof(ConsultasDb))]
    [Migration("20261123180000_ImpuestoDepartamentosTodosActivos")]
    public partial class ImpuestoDepartamentosTodosActivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO consultas.vehicle_tax_departments (id, department_code, department_name, is_active, created_at, updated_at, deleted_at) VALUES
                  ('01941215-0000-7000-8000-000000000005', '05', 'ANTIOQUIA',          true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000008', '08', 'ATLANTICO',          true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000011', '11', 'BOGOTA D.C.',        true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000013', '13', 'BOLIVAR',            true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000015', '15', 'BOYACA',             true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000017', '17', 'CALDAS',             true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000018', '18', 'CAQUETA',            true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000019', '19', 'CAUCA',              true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000020', '20', 'CESAR',              true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000023', '23', 'CORDOBA',            true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000025', '25', 'CUNDINAMARCA',       true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000027', '27', 'CHOCO',              true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000041', '41', 'HUILA',              true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000044', '44', 'LA GUAJIRA',         true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000047', '47', 'MAGDALENA',          true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000050', '50', 'META',               true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000052', '52', 'NARINO',             true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000054', '54', 'NORTE DE SANTANDER', true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000063', '63', 'QUINDIO',            true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000066', '66', 'RISARALDA',          true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000068', '68', 'SANTANDER',          true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000070', '70', 'SUCRE',              true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000073', '73', 'TOLIMA',             true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000076', '76', 'VALLE DEL CAUCA',    true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000081', '81', 'ARAUCA',             true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000085', '85', 'CASANARE',           true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000086', '86', 'PUTUMAYO',           true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000088', '88', 'SAN ANDRES',         true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000091', '91', 'AMAZONAS',           true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000094', '94', 'GUAINIA',            true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000095', '95', 'GUAVIARE',           true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000097', '97', 'VAUPES',             true, now(), now(), NULL),
                  ('01941215-0000-7000-8000-000000000099', '99', 'VICHADA',            true, now(), now(), NULL)
                ON CONFLICT (department_code) DO UPDATE SET is_active = true, deleted_at = NULL, updated_at = now();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Borra solo las filas de id determinista de los códigos que no tenían fila antes del Up; una fila previa de otro id
            // (p. ej. creada por API) se conserva y se apaga. Luego deja activos solo 05/13/17/63/68/76, como 20261120002000.
            migrationBuilder.Sql(
                """
                DELETE FROM consultas.vehicle_tax_departments
                 WHERE id IN ('01941215-0000-7000-8000-000000000015', '01941215-0000-7000-8000-000000000018',
                              '01941215-0000-7000-8000-000000000019', '01941215-0000-7000-8000-000000000020',
                              '01941215-0000-7000-8000-000000000023', '01941215-0000-7000-8000-000000000025',
                              '01941215-0000-7000-8000-000000000027', '01941215-0000-7000-8000-000000000041',
                              '01941215-0000-7000-8000-000000000044', '01941215-0000-7000-8000-000000000047',
                              '01941215-0000-7000-8000-000000000054', '01941215-0000-7000-8000-000000000066',
                              '01941215-0000-7000-8000-000000000070', '01941215-0000-7000-8000-000000000073',
                              '01941215-0000-7000-8000-000000000081', '01941215-0000-7000-8000-000000000085',
                              '01941215-0000-7000-8000-000000000086', '01941215-0000-7000-8000-000000000088',
                              '01941215-0000-7000-8000-000000000091', '01941215-0000-7000-8000-000000000095',
                              '01941215-0000-7000-8000-000000000097', '01941215-0000-7000-8000-000000000099');

                UPDATE consultas.vehicle_tax_departments
                   SET is_active = false, deleted_at = COALESCE(deleted_at, now()), updated_at = now()
                 WHERE department_code NOT IN ('05', '13', '17', '63', '68', '76')
                   AND (is_active OR deleted_at IS NULL);
                """);
        }
    }
}
