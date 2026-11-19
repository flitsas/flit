using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Flit.Comparendos.Api.Persistence.Migrations;

/// <summary>
/// HU #14367 / GDC ID12: alinea <c>numero_normalizado</c> quitando prefijos D-/A- cuando quedan ≥10 dígitos.
/// Lo que choca no se toca y queda reportado con <c>RAISE NOTICE</c> (tenant y número) para revisión manual.
/// <para>
/// Corregida en sitio (GDC punto 12, antes de llegar a <c>release</c>): la versión anterior limpiaba
/// <c>documento_ocr</c> sin revisar choques. <b>QA y DEV ya la aplicaron con esa versión</b> y no la vuelven a correr:
/// allí los documentos OCR pueden haber quedado sin prefijo aunque su comparendo lo conserve. Las consultas de
/// solo lectura de <c>docs/comparendos/simulacro-numero-sin-prefijo.md</c> lo detectan.
/// </para>
/// <para>
/// Regla, la misma para las dos tablas y evaluada sobre el estado de antes: un número (tenant, limpio) choca si ya
/// existe un comparendo con ese número sin prefijo o si dos comparendos con prefijo distinto quedarían iguales. Un
/// documento OCR se limpia solo si su número no choca, así queda igual que el comparendo que lo comparte. Dos documentos
/// OCR sí pueden quedar con el mismo número: no tienen índice único y la detección de duplicados del OCR cuenta con eso.
/// </para>
/// </summary>
public partial class GDC_NumeroComparendoSoloDigitos : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Prefijo + ≥10 dígitos (comparendo real). No toca claves cortas/sintéticas (AV3, OCRRENTING1).
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                r record;
                comparendos_limpios int;
                documentos_limpios int;
                comparendos_choque int := 0;
                documentos_choque int := 0;
            BEGIN
                CREATE TEMP TABLE gdc_candidato AS
                SELECT id, tenant_id, numero_normalizado AS numero,
                       regexp_replace(numero_normalizado, '^[A-Za-z]+', '') AS limpio
                FROM comparendos.comparendo
                WHERE numero_normalizado ~ '^[A-Za-z]+[0-9]{10,}$';

                -- (tenant, limpio) que choca: ya hay un comparendo con ese número, o llegan a él dos con prefijo distinto.
                CREATE TEMP TABLE gdc_choque AS
                SELECT DISTINCT c.tenant_id, c.limpio
                FROM gdc_candidato c
                WHERE EXISTS (SELECT 1 FROM comparendos.comparendo x
                              WHERE x.tenant_id = c.tenant_id AND x.numero_normalizado = c.limpio)
                   OR EXISTS (SELECT 1 FROM gdc_candidato o
                              WHERE o.tenant_id = c.tenant_id AND o.limpio = c.limpio AND o.id <> c.id);

                CREATE TEMP TABLE gdc_documento AS
                SELECT d.id, d.tenant_id, d.numero_normalizado AS numero,
                       regexp_replace(d.numero_normalizado, '^[A-Za-z]+', '') AS limpio
                FROM comparendos.documento_ocr d
                WHERE d.numero_normalizado ~ '^[A-Za-z]+[0-9]{10,}$';

                FOR r IN SELECT c.tenant_id, c.numero, c.limpio
                         FROM gdc_candidato c JOIN gdc_choque k USING (tenant_id, limpio)
                         ORDER BY c.tenant_id, c.limpio, c.numero LOOP
                    comparendos_choque := comparendos_choque + 1;
                    RAISE NOTICE 'GDC_NumeroComparendoSoloDigitos: comparendo sin cambiar por choque · tenant % · número % (quedaría %)',
                        r.tenant_id, r.numero, r.limpio;
                END LOOP;

                -- Un documento choca con lo mismo que un comparendo, más un comparendo sin prefijo que ya tenga su número
                -- (aunque ningún comparendo con prefijo llegue a él).
                FOR r IN SELECT d.tenant_id, d.numero, d.limpio
                         FROM gdc_documento d
                         WHERE EXISTS (SELECT 1 FROM gdc_choque k WHERE k.tenant_id = d.tenant_id AND k.limpio = d.limpio)
                            OR EXISTS (SELECT 1 FROM comparendos.comparendo x
                                       WHERE x.tenant_id = d.tenant_id AND x.numero_normalizado = d.limpio)
                         ORDER BY d.tenant_id, d.limpio, d.numero LOOP
                    documentos_choque := documentos_choque + 1;
                    RAISE NOTICE 'GDC_NumeroComparendoSoloDigitos: documento OCR sin cambiar por choque · tenant % · número % (quedaría %)',
                        r.tenant_id, r.numero, r.limpio;
                END LOOP;

                -- Los documentos primero: la regla mira los comparendos como estaban antes de limpiarlos.
                UPDATE comparendos.documento_ocr t
                SET numero_normalizado = d.limpio
                FROM gdc_documento d
                WHERE t.id = d.id
                  AND NOT EXISTS (SELECT 1 FROM gdc_choque k WHERE k.tenant_id = d.tenant_id AND k.limpio = d.limpio)
                  AND NOT EXISTS (SELECT 1 FROM comparendos.comparendo x
                                  WHERE x.tenant_id = d.tenant_id AND x.numero_normalizado = d.limpio);
                GET DIAGNOSTICS documentos_limpios = ROW_COUNT;

                UPDATE comparendos.comparendo t
                SET numero_normalizado = c.limpio
                FROM gdc_candidato c
                WHERE t.id = c.id
                  AND NOT EXISTS (SELECT 1 FROM gdc_choque k WHERE k.tenant_id = c.tenant_id AND k.limpio = c.limpio);
                GET DIAGNOSTICS comparendos_limpios = ROW_COUNT;

                RAISE NOTICE 'GDC_NumeroComparendoSoloDigitos: % comparendos y % documentos OCR sin prefijo; % comparendos y % documentos sin cambiar por choque',
                    comparendos_limpios, documentos_limpios, comparendos_choque, documentos_choque;

                DROP TABLE gdc_candidato, gdc_choque, gdc_documento;
            END
            $$;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Irreversible: los prefijos alfabéticos no se reconstruyen.
    }
}
