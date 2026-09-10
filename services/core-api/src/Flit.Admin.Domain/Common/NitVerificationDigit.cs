namespace Flit.Admin.Domain.Common;

/// <summary>
/// Dígito de verificación del NIT — módulo 11, algoritmo DIAN (CF-25, Feature #12201).
///
/// <para>El DV <b>se calcula</b>: no se consulta a ningún proveedor y no se le pide al usuario. Las
/// variables <c>{{transferente_digito_verificacion}}</c> y <c>{{adquirente_digito_verificacion}}</c>
/// del anexo normativo (<c>docs/plantilla-transferencia-dominio.md</c> §5.2/§5.3) se resuelven aquí,
/// de modo que un NIT tecleado y su DV nunca puedan discrepar.</para>
///
/// <para>Función <b>pura</b> y sin dependencias: vive en el dominio, no toca infraestructura, no lee
/// configuración y no mira el reloj. La misma entrada devuelve siempre la misma salida.</para>
///
/// <para><b>El caso especial está en el residuo.</b> Con la suma ponderada <c>S</c> y
/// <c>r = S mod 11</c>: si <c>r</c> es 0 o 1 el dígito <b>es</b> <c>r</c>; en cualquier otro caso es
/// <c>11 - r</c>. Aplicar <c>11 - r</c> sin la excepción produce 11 y 10, que no son dígitos — es el
/// error clásico de esta rutina.</para>
///
/// <para>Uso de ejemplo:</para>
/// <code>
/// NitVerificationDigit.Compute("890903938");   // 8
/// NitVerificationDigit.TryCompute("900-123.456", out var dv); // true, dv = 8
/// </code>
/// </summary>
public static class NitVerificationDigit
{
    /// <summary>
    /// Ponderaciones oficiales de la DIAN, aplicadas de DERECHA a IZQUIERDA sobre los dígitos del
    /// NIT. Son 15: el NIT colombiano no supera esa longitud.
    /// </summary>
    private static readonly int[] Weights =
        [3, 7, 13, 17, 19, 23, 29, 37, 41, 43, 47, 53, 59, 67, 71];

    /// <summary>Longitud máxima admitida, impuesta por la tabla de ponderaciones.</summary>
    public static int MaxLength => Weights.Length;

    /// <summary>
    /// Calcula el dígito de verificación (0..9) del NIT. Ignora espacios, puntos y guiones, de modo
    /// que <c>900.123.456</c> y <c>900123456</c> dan el mismo resultado.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// El NIT es nulo, vacío, trae caracteres que no son dígitos o supera <see cref="MaxLength"/>.
    /// Se lanza con el <b>nombre del parámetro</b> y sin repetir el valor recibido: el NIT es PII
    /// (Ley 1581) y el mensaje de error puede terminar en un log.
    /// </exception>
    public static int Compute(string? nit)
    {
        if (!TryCompute(nit, out var digit))
        {
            throw new ArgumentException(
                "NIT inválido: se esperan solo dígitos (máximo " + MaxLength + "), con o sin puntos y guiones.",
                nameof(nit));
        }

        return digit;
    }

    /// <summary>
    /// Variante que no lanza: <c>false</c> cuando el NIT no es calculable (nulo, vacío, con letras o
    /// demasiado largo). Es la que usan los handlers de prellenado, donde un NIT mal formado degrada
    /// la respuesta pero no la hace fallar.
    /// </summary>
    public static bool TryCompute(string? nit, out int digit)
    {
        digit = 0;

        if (string.IsNullOrWhiteSpace(nit))
        {
            return false;
        }

        // Se recorre de derecha a izquierda: la ponderación depende de la POSICIÓN desde el final,
        // no de la longitud del NIT. Así un NIT de 9 y uno de 12 dígitos usan la misma tabla sin
        // alinearla ni rellenarla con ceros a la izquierda.
        var sum = 0;
        var position = 0;

        for (var i = nit.Length - 1; i >= 0; i--)
        {
            var c = nit[i];

            if (c is ' ' or '.' or '-' or ',')
            {
                continue;
            }

            if (c is < '0' or > '9')
            {
                return false;
            }

            if (position >= Weights.Length)
            {
                return false;
            }

            sum += (c - '0') * Weights[position];
            position++;
        }

        if (position == 0)
        {
            return false;
        }

        var remainder = sum % 11;

        // El caso especial: 0 y 1 se devuelven tal cual. Con 11 - r saldrían 11 y 10.
        digit = remainder < 2 ? remainder : 11 - remainder;
        return true;
    }
}
