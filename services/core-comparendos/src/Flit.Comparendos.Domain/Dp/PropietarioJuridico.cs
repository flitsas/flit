using Flit.Comparendos.Domain.Identificadores;
using Flit.Comparendos.Domain.Modelo;

namespace Flit.Comparendos.Domain.Dp;

/// <summary>
/// HU #14618 (Epic #14606): la ÚNICA regla de «el propietario es una persona jurídica» para los derechos de petición. La usan
/// la decisión de tipología (<c>ServicioDp.DecidirAsync</c>) y el resolutor del representante (<c>PrepararAsync</c>), para que no
/// diverjan. Es persona jurídica si el tipo de documento del infractor es NIT, O si el tipo viene vacío y el número de documento del
/// infractor es el NIT consultado (la fuente municipal por NIT a veces no trae el tipo, pero el dueño es justo el NIT que se
/// consultó). Sin esa evidencia NO se asume; un tipo distinto de NIT nunca cambia.
/// </summary>
public static class PropietarioJuridico
{
    public static bool Es(TipoDocumento? tipo, string? numeroDocumentoInfractor, string? nitConsultado)
    {
        if (tipo is { } t)
            return t == TipoDocumento.NIT;
        var numero = Digitos(numeroDocumentoInfractor);
        var nit = Digitos(nitConsultado);
        if (numero.Length == 0 || nit.Length == 0)
            return false;
        if (numero == nit)
            return true;
        return nit.Length <= 15 && numero == nit + Nit.DigitoVerificacion(nit).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Digitos(string? texto) => new string((texto ?? string.Empty).Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
}
