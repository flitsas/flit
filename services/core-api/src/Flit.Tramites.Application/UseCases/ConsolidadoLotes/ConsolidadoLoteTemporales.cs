namespace Flit.Tramites.Application.UseCases.ConsolidadoLotes;

/// <summary>
/// HU #13378 (ADR-0070 D4, AC5, M2) — directorio temporal del carril de empaquetado. Cada parte usa como mucho dos
/// archivos a la vez (el ZIP en claro y su versión cifrada FLZ1): el disco temporal queda en ≤ 2 × M más la sobrecarga
/// del cifrado (20 B por MiB) y de las cabeceras ZIP. El ZIP en claro se borra en cuanto se cifra.
/// <list type="bullet">
///   <item>Por defecto <c>{Path.GetTempPath()}/flit-consolidado-lotes</c>: en Linux <c>GetTempPath</c> respeta
///   <c>TMPDIR</c>, que es lo que monta el volumen dedicado (nota de infra de la HU).</item>
///   <item>Solo se crean archivos con las extensiones <see cref="ExtensionZip"/> y <see cref="ExtensionCifrado"/>, y la
///   limpieza de arranque (<see cref="LimpiarHuerfanos"/>) solo borra esas dos dentro de este subdirectorio.</item>
///   <item>Los nombres llevan solo ids técnicos y un sufijo aleatorio: nunca placa ni radicado.</item>
/// </list>
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var tmp = ConsolidadoLoteTemporales.Predeterminado();
/// var (zip, cifrado) = tmp.NuevasRutas(loteId, 2);
/// try { … } finally { ConsolidadoLoteTemporales.BorrarSilencioso(zip); ConsolidadoLoteTemporales.BorrarSilencioso(cifrado); }
/// </code>
/// </remarks>
public sealed class ConsolidadoLoteTemporales
{
    /// <summary>Subdirectorio propio del carril dentro del temporal del sistema.</summary>
    public const string Subdirectorio = "flit-consolidado-lotes";

    public const string ExtensionZip = ".zip.tmp";

    public const string ExtensionCifrado = ".flz.tmp";

    public ConsolidadoLoteTemporales(string directorio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directorio);
        Directorio = Path.GetFullPath(directorio);
    }

    /// <summary>Ruta absoluta del directorio del carril.</summary>
    public string Directorio { get; }

    /// <summary>Directorio por defecto: <c>{TMPDIR}/flit-consolidado-lotes</c>.</summary>
    public static ConsolidadoLoteTemporales Predeterminado() =>
        new(Path.Combine(Path.GetTempPath(), Subdirectorio));

    /// <summary>Rutas nuevas (aún inexistentes) para el ZIP en claro y el cifrado de una parte. Crea el directorio.</summary>
    public (string Zip, string Cifrado) NuevasRutas(Guid loteId, short partNumber)
    {
        Directory.CreateDirectory(Directorio);
        var baseNombre = $"{loteId:N}-p{partNumber}-{Guid.NewGuid():N}";
        return (Path.Combine(Directorio, baseNombre + ExtensionZip), Path.Combine(Directorio, baseNombre + ExtensionCifrado));
    }

    /// <summary>
    /// AC5 — al arrancar no hay ninguna parte en vuelo en este proceso: todo temporal del carril es huérfano de una
    /// ejecución anterior (caída, <c>docker kill</c>). Borra solo los archivos del carril; un archivo que no se pueda
    /// borrar se salta. Devuelve cuántos borró.
    /// </summary>
    public int LimpiarHuerfanos()
    {
        if (!Directory.Exists(Directorio))
            return 0;

        var borrados = 0;
        foreach (var archivo in Directory.EnumerateFiles(Directorio))
        {
            if (!EsDelCarril(archivo))
                continue;
            if (BorrarSilencioso(archivo))
                borrados++;
        }

        return borrados;
    }

    /// <summary>Borra el archivo si existe. <c>true</c> si lo borró. Nunca lanza.</summary>
    public static bool BorrarSilencioso(string? ruta)
    {
        if (string.IsNullOrEmpty(ruta))
            return false;
        try
        {
            if (!File.Exists(ruta))
                return false;
            File.Delete(ruta);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool EsDelCarril(string archivo) =>
        archivo.EndsWith(ExtensionZip, StringComparison.Ordinal)
        || archivo.EndsWith(ExtensionCifrado, StringComparison.Ordinal);
}
