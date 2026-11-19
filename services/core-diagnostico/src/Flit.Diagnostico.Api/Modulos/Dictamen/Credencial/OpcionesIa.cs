namespace Flit.Diagnostico.Api.Modulos.Dictamen.Credencial;

/// <summary>
/// <c>Diagnostico:Ia</c> (ADR-0091 §3.1). <see cref="Origen"/> <c>local</c> (por defecto, desarrollo) usa <c>Diagnostico:Anthropic</c>;
/// <c>plataforma</c> (servidores) toma proveedor, modelo y llave de «Configuración de IA» de core-api por gRPC, con la llave cifrada para
/// la privada de <see cref="ClavePrivadaArchivo"/> (SP-16: docker secret, DER PKCS#8 en base64 o PEM). Nunca simula.
/// </summary>
public sealed class OpcionesIa
{
    public const string Seccion = "Diagnostico:Ia";

    public const string OrigenLocal = "local";

    public const string OrigenPlataforma = "plataforma";

    /// <summary>Modelo por defecto de la allowlist local (SP-14 = a).</summary>
    public const string ModeloPorDefecto = "claude-haiku-4-5";

    public const int MaxLlamadasPorMinutoPorDefecto = 30;

    public string Origen { get; set; } = OrigenLocal;

    /// <summary>Ruta del archivo con la clave privada actual (p. ej. <c>/run/secrets/diagnostico_ia_privada</c>).</summary>
    public string ClavePrivadaArchivo { get; set; } = string.Empty;

    /// <summary><c>kid</c> de la clave privada actual (<c>diag-&lt;ambiente&gt;-&lt;aaaamm&gt;</c>).</summary>
    public string Kid { get; set; } = string.Empty;

    /// <summary>Clave anterior durante una rotación (§2.6). Opcional.</summary>
    public string ClavePrivadaAnteriorArchivo { get; set; } = string.Empty;

    public string KidAnterior { get; set; } = string.Empty;

    /// <summary>Modelos que Diagnóstico acepta de core-api (SP-14 = a). Vacío o ausente = solo <see cref="ModeloPorDefecto"/>.</summary>
#pragma warning disable CA1819 // Lista de configuración: el binder la reemplaza entera.
    public string[]? ModelosPermitidos { get; set; }
#pragma warning restore CA1819

    /// <summary>Tope local de llamadas por minuto al proveedor (SP-17). Un valor no positivo vale el defecto.</summary>
    public int MaxLlamadasPorMinuto { get; set; } = MaxLlamadasPorMinutoPorDefecto;

    /// <summary>Plazo de cada llamada gRPC a core-api.</summary>
    public int PlazoRpcSegundos { get; set; } = 10;

    public IReadOnlyList<string> ModelosEfectivos =>
        ModelosPermitidos?.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()).ToArray() is { Length: > 0 } lista
            ? lista
            : [ModeloPorDefecto];

    /// <summary>SP-16: solo origen, kid y si hay privada configurada; nunca la ruta ni el contenido.</summary>
    public override string ToString() =>
        $"OpcionesIa(origen={Origen}, kid={Kid}, privada={(string.IsNullOrWhiteSpace(ClavePrivadaArchivo) ? "ausente" : "presente")}, " +
        $"kid_anterior={(string.IsNullOrWhiteSpace(KidAnterior) ? "-" : KidAnterior)}, modelos={string.Join(',', ModelosEfectivos)}, max_por_minuto={MaxLlamadasPorMinuto})";
}
