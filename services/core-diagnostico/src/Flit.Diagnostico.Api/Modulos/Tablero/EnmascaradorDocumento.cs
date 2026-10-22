namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Enmascara el documento de una persona para el detalle de la celda (HU #13798, F06-C2): <c>****</c> más los cuatro últimos
/// caracteres alfanuméricos (sin guiones ni espacios: el NIT <c>900123456-7</c> queda <c>****4567</c>). Con cuatro caracteres o
/// menos solo <c>****</c>, para que nunca se vea completo. El documento en claro se descifra solo para esto y no sale de aquí.
/// </summary>
/// <example>
/// <code>
/// EnmascaradorDocumento.Enmascarar("1012345678"); // "****5678"
/// </code>
/// </example>
internal static class EnmascaradorDocumento
{
    public const string Mascara = "****";

    private const int Visibles = 4;

    public static string Enmascarar(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento))
            return Mascara;
        var limpio = new string([.. documento.Where(char.IsLetterOrDigit)]);
        return limpio.Length <= Visibles ? Mascara : Mascara + limpio[^Visibles..];
    }
}
