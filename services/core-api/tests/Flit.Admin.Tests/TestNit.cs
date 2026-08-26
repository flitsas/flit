namespace Flit.Admin.Tests;

/// <summary>
/// NITs de prueba únicos por ejecución para los tests que seedean <c>identity.tenants</c>
/// contra PostgreSQL real.
///
/// Desde la migración <c>NitUnicoPorCompania</c> existe el índice único parcial
/// <c>uq_tenants_tax_id</c> sobre <c>tax_id</c>, y todas las clases de integración comparten
/// la MISMA base (<c>flit_dev</c> en CI). Un NIT literal repetido entre dos clases —o entre dos
/// ejecuciones que dejan datos— revienta con <c>23505 duplicate key</c> según cómo xUnit
/// paralelice las colecciones, convirtiendo la suite en intermitente. Los tests ya usan GUIDs
/// aleatorios para ids, correos y códigos: el NIT es el último literal que faltaba.
/// </summary>
internal static class TestNit
{
    // Prefijo aleatorio por proceso (9 dígitos) + secuencia: único dentro del ensamblado y, en la
    // práctica, también frente a los otros ensamblados de test que comparten la base.
    private static readonly long Prefix = Random.Shared.NextInt64(100_000_000L, 999_999_999L);
    private static int _seq;

    /// <summary>
    /// Genera un NIT único de 12 dígitos (cabe de sobra en <c>tax_id varchar(20)</c>).
    /// </summary>
    public static string Unique() =>
        $"{Prefix}{Interlocked.Increment(ref _seq):D3}";
}
