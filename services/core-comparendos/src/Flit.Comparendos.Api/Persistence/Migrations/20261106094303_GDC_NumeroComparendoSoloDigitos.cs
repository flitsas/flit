using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Comparendos.Api.Persistence.Migrations;

/// <summary>
/// HU #14367 / GDC ID12: alinea <c>numero_normalizado</c> quitando prefijos D-/A- cuando quedan ≥10 dígitos.
/// Filas con colisión de índice único (tenant_id, numero_normalizado) se dejan sin cambiar para revisión manual.
/// </summary>
public partial class GDC_NumeroComparendoSoloDigitos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Prefijo + ≥10 dígitos (comparendo real). No toca claves cortas/sintéticas (AV3, OCRRENTING1).
        migrationBuilder.Sql("""
            WITH candidatos AS (
                SELECT id, tenant_id, numero_normalizado,
                       regexp_replace(numero_normalizado, '^[A-Za-z]+', '') AS limpio
                FROM comparendos.comparendo
                WHERE numero_normalizado ~ '^[A-Za-z]+[0-9]{10,}$'
            ),
            sin_colision AS (
                SELECT c.id, c.limpio
                FROM candidatos c
                WHERE c.limpio <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM comparendos.comparendo x
                      WHERE x.tenant_id = c.tenant_id
                        AND x.numero_normalizado = c.limpio
                        AND x.id <> c.id)
                  AND NOT EXISTS (
                      SELECT 1 FROM candidatos o
                      WHERE o.tenant_id = c.tenant_id
                        AND o.limpio = c.limpio
                        AND o.id <> c.id)
            )
            UPDATE comparendos.comparendo t
            SET numero_normalizado = s.limpio
            FROM sin_colision s
            WHERE t.id = s.id;

            UPDATE comparendos.documento_ocr
            SET numero_normalizado = regexp_replace(numero_normalizado, '^[A-Za-z]+', '')
            WHERE numero_normalizado ~ '^[A-Za-z]+[0-9]{10,}$';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Irreversible: los prefijos alfabéticos no se reconstruyen.
    }
}
