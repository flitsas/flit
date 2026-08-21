using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Tramites.Domain.Documents;

/// <summary>
/// Objeto del Contrato Privado de Mandato (<c>{{tramite}}</c>).
/// Fuente canónica: <c>docs/ot/mandato/REGLAS-OBJETO-TRES-CAPAS.md</c>.
/// Las 4 plantillas interpolan este string; no duplican las capas.
/// </summary>
public static class MandatoObjetoComposer
{
    public const string CambioColor = "cambio_color";
    public const string CambioCarroceria = "cambio_carroceria";
    public const string CambioCombustible = "cambio_combustible";
    public const string Blindaje = "blindaje";

    /// <summary>Complemento tabla 2 — inscripción (no el trámite base <c>INSCRIBIR PRENDA</c>).</summary>
    public const string Prenda = "INSCRIPCIÓN DE PRENDA";

    /// <summary>Complemento tabla 2 — levantamiento (no el trámite base <c>LEVANTAR PRENDA</c>).</summary>
    public const string LevantamientoPrenda = "LEVANTAMIENTO DE PRENDA";

    public const string ConversionCombustible = "CONVERSIONES DE COMBUSTIBLE";

    private static readonly (string Clave, string Etiqueta)[] Etiquetas =
    [
        (CambioColor, "CAMBIO DE COLOR"),
        (CambioCarroceria, "CAMBIO DE CARROCERÍA"),
        (CambioCombustible, ConversionCombustible),
        (Blindaje, "BLINDAJE"),
    ];

    /// <summary>
    /// Compone <c>{{tramite}}</c>: tabla 1 + prenda complementaria + transformaciones.
    /// Fórmula: un fragmento; dos → <c>A CON B</c>; más → <c>A CON B Y C Y …</c>.
    /// </summary>
    public static string Componer(
        string nombreTramite,
        IEnumerable<string>? transformaciones,
        FurPrendaMarking prendaMarking = FurPrendaMarking.Ninguna,
        string? procedureTypeCode = null)
    {
        var nombre = nombreTramite?.Trim() ?? string.Empty;
        var code = procedureTypeCode?.Trim() ?? string.Empty;
        var skipPrenda = EsTipoPrendaBase(code);
        var etiquetas = new List<string>();

        if (!skipPrenda)
        {
            switch (prendaMarking)
            {
                case FurPrendaMarking.Constitucion:
                    etiquetas.Add(Prenda);
                    break;
                case FurPrendaMarking.Levantamiento:
                    etiquetas.Add(LevantamientoPrenda);
                    break;
                case FurPrendaMarking.Ambos:
                    etiquetas.Add(LevantamientoPrenda);
                    etiquetas.Add(Prenda);
                    break;
            }
        }

        if (transformaciones is not null)
        {
            var activas = new HashSet<string>(
                transformaciones.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var (clave, etiqueta) in Etiquetas)
            {
                if (EsMismaTransformacionBase(code, clave))
                    continue;
                if (activas.Contains(clave))
                    etiquetas.Add(etiqueta);
            }
        }

        if (etiquetas.Count == 0)
            return nombre;

        var todos = new List<string>(etiquetas.Count + 1) { nombre };
        todos.AddRange(etiquetas);

        if (todos.Count == 2)
            return $"{todos[0]} CON {todos[1]}";

        return $"{todos[0]} CON {string.Join(" Y ", todos.Skip(1))}";
    }

    private static bool EsTipoPrendaBase(string code) =>
        code.Equals("PRENDA_INSCRIPCION", StringComparison.OrdinalIgnoreCase)
        || code.Equals("LEVANTAMIENTO_PRENDA", StringComparison.OrdinalIgnoreCase)
        || code.Equals("LEVANTAR_INSCRIBIR_PRENDA", StringComparison.OrdinalIgnoreCase);

    private static bool EsMismaTransformacionBase(string code, string clave)
    {
        if (string.IsNullOrEmpty(code))
            return false;

        if (clave.Equals(CambioColor, StringComparison.OrdinalIgnoreCase))
            return code.Equals("CAMBIO_COLOR", StringComparison.OrdinalIgnoreCase);
        if (clave.Equals(CambioCarroceria, StringComparison.OrdinalIgnoreCase))
            return code.Equals("CAMBIO_CARROCERIA", StringComparison.OrdinalIgnoreCase);
        if (clave.Equals(CambioCombustible, StringComparison.OrdinalIgnoreCase))
            return code.Equals("CONVERSION_COMBUSTIBLE", StringComparison.OrdinalIgnoreCase);
        if (clave.Equals(Blindaje, StringComparison.OrdinalIgnoreCase))
            return code.Contains("BLINDAJE", StringComparison.OrdinalIgnoreCase);
        return false;
    }
}
