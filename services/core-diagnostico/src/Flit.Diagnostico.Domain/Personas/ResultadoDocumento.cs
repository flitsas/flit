namespace Flit.Diagnostico.Domain.Personas;

/// <summary>
/// Resultado de <see cref="ValidadorDocumentoPersona.Validar"/> (HU #13749, F02-H4): el tipo y el número normalizados y, si
/// la forma no es válida, el código y el mensaje del error. Sin tipo ni número, el documento está ausente (es opcional).
/// </summary>
/// <param name="Tipo">Código del tipo (CC, CE, NIT, PAS o TI); null si no vino o no es válido.</param>
/// <param name="Numero">Número sin espacios en los extremos (PAS en mayúsculas); null si no vino.</param>
/// <param name="Codigo">Código estable del error de forma; null si el documento es válido o está ausente.</param>
/// <param name="Mensaje">Mensaje para el usuario; null si no hay error.</param>
public sealed record ResultadoDocumento(string? Tipo, string? Numero, string? Codigo, string? Mensaje)
{
    /// <summary>Documento ausente: ni tipo ni número.</summary>
    public static ResultadoDocumento Ausente { get; } = new(null, null, null, null);

    /// <summary>Verdadero si no hay error de forma (válido o ausente).</summary>
    public bool EsValido => Codigo is null;

    /// <summary>Verdadero si el documento vino completo y con forma válida.</summary>
    public bool Presente => EsValido && Tipo is not null && Numero is not null;
}
